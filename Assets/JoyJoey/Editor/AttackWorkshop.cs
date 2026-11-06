using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JoyJoey.Sandbox.Editor
{
    /// <summary>Non-destructive attack authoring. Edits a draft catalog; only Save writes the asset.</summary>
    public sealed class AttackWorkshop : EditorWindow
    {
        [SerializeField] CombatCatalog source, draft;
        [SerializeField] int moveIndex, trackIndex, frame, inspectorTab;
        [SerializeField] string search = "";
        [SerializeField] float zoom = 92;
        [SerializeField] bool mirror, useClip = true;
        [SerializeField] string savedDraftJson;
        [SerializeField] string savedSourceJson;
        string observedDraftJson;
        Vector2 moveScroll, inspectorScroll;
        SerializedObject serialized;
        double previousTime, playbackRemainder;
        bool playing, dragging, resizing;
        int dragControl, dragUndoGroup;
        Vector2 dragStart, originalOffset;
        HitboxKeyframe dragKey;
        Rect canvas;
        Vector2 origin;
        PreviewRenderUtility preview;
        GameObject previewInstance, previewAsset;
        static readonly Color Teal = new Color(.29f,.87f,.80f);
        static readonly Color Gold = new Color(1,.73f,.25f);
        static readonly Color Coral = new Color(1,.45f,.39f);
        static readonly Color Violet = new Color(.68f,.51f,.96f);
        GUIStyle titleStyle, tinyStyle;
        MoveDefinition Move => draft != null && moveIndex >= 0 && moveIndex < draft.Moves.Count ? draft.Moves[moveIndex] : null;
        HitboxTrack Track => Move != null && Move.Hitboxes != null && trackIndex >= 0 && trackIndex < Move.Hitboxes.Count ? Move.Hitboxes[trackIndex] : null;

        [MenuItem("Joy Joey/Taller de ataques")]
        public static void Open() { GetWindow<AttackWorkshop>("Taller de ataques").Show(); }

        public static void Open(CombatCatalog catalog)
        {
            var window = GetWindow<AttackWorkshop>("Taller de ataques");
            if (window.source != catalog && window.ConfirmDraftChange()) window.Load(catalog);
            window.Show();
        }

        [OnOpenAsset]
        static bool OnOpenAsset(int instanceId, int line)
        {
#pragma warning disable CS0618 // The OnOpenAsset callback supplies a legacy instance ID.
            var catalog = EditorUtility.InstanceIDToObject(instanceId) as CombatCatalog;
#pragma warning restore CS0618
            if (!catalog) return false;
            Open(catalog); return true;
        }

        void OnEnable()
        {
            minSize = new Vector2(1040, 670);
            saveChangesMessage = "Hay cambios de ataques sin guardar. Guardar actualiza el catálogo; descartar conserva el asset actual.";
            if (!source) source = AssetDatabase.LoadAssetAtPath<CombatCatalog>("Assets/JoyJoey/Data/SandboxCombat.asset");
            if (!draft && source) Load(source);
            else if (draft) { serialized = new SerializedObject(draft); observedDraftJson = JsonUtility.ToJson(draft); }
            previousTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += EditorTick;
            Undo.undoRedoPerformed += UndoChanged;
        }

        void OnDisable()
        {
            EditorApplication.update -= EditorTick;
            Undo.undoRedoPerformed -= UndoChanged;
            CleanupPreview();
        }

        void OnDestroy() { if (draft) { Undo.ClearUndo(draft); DestroyImmediate(draft); } }

        void OnLostFocus()
        {
            if(!dragging)return;
            Undo.CollapseUndoOperations(dragUndoGroup);dragging=false;dragKey=null;GUIUtility.hotControl=0;
        }

        void UndoChanged()
        {
            if (draft)
            {
                serialized = new SerializedObject(draft);
                string current = JsonUtility.ToJson(draft);
                if (current != observedDraftJson) MarkChanged();
            }
            ClampSelection(); Repaint();
        }

        void EditorTick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (playing && Move != null)
            {
                playbackRemainder += Math.Min(.2, now - previousTime) * 60;
                int advance = (int)playbackRemainder;
                if (advance > 0) { frame = (frame + advance) % Move.Total; playbackRemainder -= advance; Repaint(); }
            }
            previousTime = now;
        }

        void Load(CombatCatalog catalog)
        {
            playing = false; CleanupPreview();
            if (draft) { Undo.ClearUndo(draft); DestroyImmediate(draft); }
            source = catalog; draft = CreateDraft(source);
            if (draft) serialized = new SerializedObject(draft);
            else serialized = null;
            savedDraftJson = observedDraftJson = draft ? JsonUtility.ToJson(draft) : null;
            savedSourceJson = source ? JsonUtility.ToJson(source) : null;
            moveIndex = trackIndex = frame = 0; hasUnsavedChanges = false; Repaint();
        }

        bool ConfirmDraftChange()
        {
            if (!hasUnsavedChanges) return true;
            int result = EditorUtility.DisplayDialogComplex("Cambios sin guardar", saveChangesMessage, "Guardar", "Cancelar", "Descartar");
            if (result == 1) return false;
            if (result == 0) return SaveDraft();
            return true;
        }

        public override void SaveChanges()
        {
            if (!SaveDraft()) throw new InvalidOperationException("Corrige los errores indicados antes de guardar el catálogo.");
            base.SaveChanges();
        }

        public override void DiscardChanges() { Load(source); base.DiscardChanges(); }

        bool SaveDraft()
        {
            if (!source || !draft) return false;
            if (savedSourceJson == null)
            { ShowNotification(new GUIContent("Este borrador no tiene una referencia de la fuente. Usa Recargar antes de guardar.")); return false; }
            if (!CommitDraft(draft,source,out string error,savedSourceJson)) { ShowNotification(new GUIContent(error)); return false; }
            savedDraftJson = observedDraftJson = JsonUtility.ToJson(draft); hasUnsavedChanges = false;
            savedSourceJson = JsonUtility.ToJson(source);
            ShowNotification(new GUIContent("Catálogo guardado. Reinicia Play para probar los cambios."));
            return true;
        }

        public static CombatCatalog CreateDraft(CombatCatalog catalog)
        {
            if (!catalog) return null;
            var copy = Instantiate(catalog); copy.name = catalog.name + " (borrador)"; copy.hideFlags = HideFlags.HideAndDontSave;
            return copy;
        }

        /// <summary>Explicit commit boundary, shared by Save and the editor round-trip validation.</summary>
        public static bool CommitDraft(CombatCatalog currentDraft,CombatCatalog asset,out string error,string expectedSourceJson=null)
        {
            error = Validate(currentDraft);
            if (error != null) return false;
            if (!asset || !AssetDatabase.Contains(asset)) { error = "El destino debe ser un asset de catálogo guardado."; return false; }
            if (asset.SchemaVersion > currentDraft.SchemaVersion)
            { error = "El catálogo fuente tiene una versión de esquema más nueva que el borrador. Usa Recargar para conservar la actualización."; return false; }
            if (expectedSourceJson != null && !string.Equals(expectedSourceJson,JsonUtility.ToJson(asset),StringComparison.Ordinal))
            { error = "La fuente cambió fuera del Taller. Usa Recargar y descarta el borrador para cargar la versión actual; no se sobrescribió el asset."; return false; }
            Undo.RecordObject(asset,"Guardar catálogo de ataques");
            string originalName = asset.name; HideFlags originalFlags = asset.hideFlags;
            EditorUtility.CopySerialized(currentDraft,asset);asset.name = originalName;asset.hideFlags = originalFlags;
            EditorUtility.SetDirty(asset);AssetDatabase.SaveAssetIfDirty(asset);return true;
        }

        /// <summary>Used by the window and editor validation; never changes the supplied catalog.</summary>
        public static string Validate(CombatCatalog catalog)
        {
            if (!catalog) return "Selecciona un catálogo.";
            var ids = new HashSet<string>();
            foreach (var move in catalog.Moves)
            {
                if (move == null || string.IsNullOrWhiteSpace(move.Id)) return "Todos los ataques necesitan un ID.";
                if (!ids.Add(move.Id)) return "ID duplicado: " + move.Id;
                if (move.Startup < 0 || move.Active < 0 || move.Recovery < 0) return move.Id + ": las fases no pueden ser negativas.";
                if (move.MomentumRetention < 0 || move.MomentumRetention > 1 || move.VerticalMomentumRetention < 0 || move.VerticalMomentumRetention > 1 || move.GravityScale <= 0 || move.AnimationSpeed <= 0)
                    return move.Id + ": revisa inercia [0,1], gravedad y velocidad de animación positivas.";
                if (move.AnimationClip != null && move.PreviewPrefab == null)
                    return move.Id + ": asigna un PreviewPrefab visual compatible con el AnimationClip para que el Taller y el juego muestren la misma animación.";
                if (move.FollowOnHold && (move.FollowFrame < 0 || move.FollowFrame >= move.Total)) return move.Id + ": el seguimiento debe ocurrir dentro del movimiento.";
                if (move.Cancels != CancelActions.None && (move.CancelOpen < 0 || move.CancelClose < move.CancelOpen || move.CancelClose >= move.Total)) return move.Id + ": ventana de cancel fuera del movimiento.";
                if (move.Hitboxes == null) continue;
                foreach (var track in move.Hitboxes)
                {
                    if (track == null || track.Hit == null) return move.Id + ": hay una hitbox sin propiedades.";
                    var hit=track.Hit;
                    if(hit.Damage<0 || hit.Hitstun<0 || hit.Hitstop<0 || hit.Rehit<0 || hit.JugglePower<0 || hit.ArmorDamage<0)
                        return move.Id + ": daño, stun, stop, reimpacto, juggle y rotura no pueden ser negativos.";
                    if(hit.PreferredDistance<0 || hit.MaxCorrection<0 || hit.CorrectionSpeed<=0 || hit.PlayerCorrectionShare<0 || hit.PlayerCorrectionShare>1)
                        return move.Id + ": parámetros de corrección inválidos.";
                    if (track.StartFrame < 0 || track.EndFrame <= track.StartFrame || track.EndFrame > move.Total)
                        return move.Id + ": la ventana de hitbox debe estar dentro de 0–" + move.Total + " (fin exclusivo).";
                    if (track.StartFrame < move.Startup || track.EndFrame > move.Startup + move.Active)
                        return move.Id + ": la hitbox debe quedar dentro de Active [" + move.Startup + ", " + (move.Startup+move.Active) + ").";
                    if (track.Keys == null || track.Keys.Count == 0) return move.Id + ": cada hitbox necesita al menos una key.";
                    if (track.Keys.Any(k => k == null || k.Frame < 0 || k.Size.x <= 0 || k.Size.y <= 0)) return move.Id + ": hay una key de hitbox inválida.";
                    if (track.Keys.GroupBy(k => k.Frame).Any(g => g.Count() > 1)) return move.Id + ": hay keys repetidas en el mismo frame.";
                }
                if (move.Poses != null && move.Poses.Any(p => p == null || p.Frame < 0)) return move.Id + ": hay una pose inválida.";
                if (move.Poses != null && move.Poses.GroupBy(p => p.Frame).Any(g => g.Count() > 1)) return move.Id + ": hay poses repetidas en el mismo frame.";
            }
            if (catalog.Bindings != null)
                foreach (var binding in catalog.Bindings)
                {
                    if (binding == null || binding.Sequence == null || binding.Sequence.Count == 0) return "Un binding no tiene movimientos en Sequence.";
                    if (binding.Action != ActionId.Basic && binding.Action != ActionId.Rag && binding.Action != ActionId.Trick)
                        return "Binding «" + binding.Name + "»: usa Basic, Rag o Trick. Salto, dash y las demás acciones pertenecen al motor de movimiento.";
                    foreach (string id in binding.Sequence) if (!ids.Contains(id ?? "")) return "Binding «" + binding.Name + "»: no existe el ID «" + id + "».";
                    if (!string.IsNullOrEmpty(binding.ChargedMoveId) && !ids.Contains(binding.ChargedMoveId)) return "Binding «" + binding.Name + "»: versión cargada inexistente.";
                }
            return null;
        }

        void OnGUI()
        {
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 };
                tinyStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            }
            DrawToolbar();
            if (!draft)
            {
                EditorGUILayout.HelpBox("Selecciona un Combat Catalog. También puedes crear uno desde Assets → Create → Joy Joey → Combat Catalog.", MessageType.Info);
                return;
            }
            ClampSelection();
            serialized.Update();
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawMoveList();
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true))) DrawCentre();
                DrawInspector();
            }
            HandleShortcuts();
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("JOY JOEY / ATTACK WORKSHOP", EditorStyles.boldLabel, GUILayout.Width(224));
                var chosen = (CombatCatalog)EditorGUILayout.ObjectField(source, typeof(CombatCatalog), false, GUILayout.MinWidth(180));
                if (chosen != source && ConfirmDraftChange()) Load(chosen);
                GUILayout.Label(hasUnsavedChanges ? "● Borrador" : "Guardado", EditorStyles.miniLabel, GUILayout.Width(75));
                using (new EditorGUI.DisabledScope(!source || !hasUnsavedChanges))
                    if (GUILayout.Button("Guardar  Ctrl+S", EditorStyles.toolbarButton, GUILayout.Width(120))) SaveDraft();
                if (GUILayout.Button("Recargar", EditorStyles.toolbarButton, GUILayout.Width(75)) && ConfirmDraftChange()) Load(source);
            }
            EditorGUILayout.LabelField("60 frames = 1 s. Abrir o previsualizar no modifica el asset. Los cambios se aplican al guardar y reiniciar Play.", tinyStyle);
        }

        void DrawMoveList()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(190), GUILayout.ExpandHeight(true)))
            {
                search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("+ Nuevo")) AddMove();
                    using (new EditorGUI.DisabledScope(Move == null)) if (GUILayout.Button("Duplicar")) DuplicateMove();
                }
                moveScroll = EditorGUILayout.BeginScrollView(moveScroll);
                for (int i = 0; i < draft.Moves.Count; i++)
                {
                    var move = draft.Moves[i]; if (move == null) continue;
                    string label = (move.Finisher ? "◆ " : "") + (string.IsNullOrEmpty(move.Title) ? move.Id : move.Title);
                    if (!string.IsNullOrEmpty(search) && (move.Id + " " + label).IndexOf(search,StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var style = new GUIStyle(EditorStyles.miniButton) { alignment = TextAnchor.MiddleLeft, wordWrap = true, fixedHeight = 39 };
                    Color old = GUI.backgroundColor; if (i == moveIndex) GUI.backgroundColor = Teal;
                    if (GUILayout.Button(label + "\n" + move.Id, style)) { moveIndex = i; trackIndex = frame = 0; playing = false; CleanupPreview(); GUI.FocusControl(null); }
                    GUI.backgroundColor = old;
                }
                EditorGUILayout.EndScrollView();
                using (new EditorGUI.DisabledScope(Move == null))
                    if (GUILayout.Button("Eliminar ataque")) DeleteMove();
            }
        }

        void DrawCentre()
        {
            if (Move == null) { EditorGUILayout.HelpBox("Crea un movimiento para empezar.",MessageType.Info); return; }
            GUILayout.Label(Move.Title ?? Move.Id, titleStyle);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(playing ? "Pausa" : "▶ Preview",GUILayout.Width(90))) playing = !playing;
                if (GUILayout.Button("|◀",GUILayout.Width(32))) { playing = false; frame = 0; }
                if (GUILayout.Button("−1",GUILayout.Width(32))) { playing = false; frame = Mathf.Max(0,frame-1); }
                if (GUILayout.Button("+1",GUILayout.Width(32))) { playing = false; frame = Mathf.Min(Move.Total-1,frame+1); }
                GUILayout.Label(frame + "f / " + (Move.Total - 1) + "f",GUILayout.Width(82));
                GUILayout.FlexibleSpace(); mirror = GUILayout.Toggle(mirror,"Espejar");
            }
            frame = EditorGUILayout.IntSlider("Frame",frame,0,Mathf.Max(0,Move.Total-1));
            float available = Mathf.Max(220,position.height-400);
            canvas = GUILayoutUtility.GetRect(300,available,GUILayout.ExpandWidth(true));
            DrawCanvas();
            using (new EditorGUILayout.HorizontalScope())
            {
                zoom = EditorGUILayout.Slider("Zoom",zoom,45,145);
                using (new EditorGUI.DisabledScope(!Move.AnimationClip || !Move.PreviewPrefab)) useClip = GUILayout.Toggle(useClip,"Clip + prefab");
            }
            DrawTimeline();
            GUILayout.Label("Arrastra el centro para mover la caja; el cuadrado de esquina cambia su tamaño. Se crea una key en el frame actual si no existe. Dorado: activa; gris: ghost seleccionada.",tinyStyle);
        }

        void DrawCanvas()
        {
            Rect layoutCanvas = canvas;
            GUI.BeginGroup(layoutCanvas);
            canvas = new Rect(Vector2.zero,layoutCanvas.size);
            origin = new Vector2(canvas.center.x - zoom * .7f,canvas.yMax - zoom * .65f);
            EditorGUI.DrawRect(canvas,new Color(.045f,.065f,.09f));
            bool rendered = useClip && Move.AnimationClip && Move.PreviewPrefab;
            if (rendered) DrawClipPreview();
            Handles.BeginGUI();
            Color original = Handles.color;
            for (int x = -5; x <= 6; x++) DrawLine(World(new Vector2(x,-1)),World(new Vector2(x,6)),new Color(.19f,.26f,.31f,.4f));
            for (int y = 0; y <= 6; y++) DrawLine(World(new Vector2(-6,y)),World(new Vector2(6,y)),new Color(.19f,.26f,.31f,.4f));
            DrawLine(new Vector2(canvas.x,origin.y),new Vector2(canvas.xMax,origin.y),Teal);
            DrawPuppet(new Vector2(1.55f,0),null,Coral,false);
            if (!rendered) DrawPuppet(Vector2.zero,Move.SamplePose(frame),Teal,mirror);
            DrawBox(new Rect(-.4f,0,.8f,1.65f),new Color(Teal.r,Teal.g,Teal.b,.45f),false);
            if (Move.Hitboxes != null)
                for (int i = 0; i < Move.Hitboxes.Count; i++)
                {
                    var track = Move.Hitboxes[i]; if (track == null || (!track.IsActive(frame) && i != trackIndex)) continue;
                    Rect box = track.Sample(frame); if (mirror) box.x = -box.xMax;
                    Color color = track.IsActive(frame) ? Gold : new Color(.58f,.64f,.7f,.6f);
                    DrawBox(box,color,true);
                    if (i != trackIndex) continue;
                    Rect pixels = WorldRect(box);
                    Vector2 corner = new Vector2(pixels.xMax,pixels.yMin);
                    EditorGUI.DrawRect(new Rect(corner-Vector2.one*4,Vector2.one*8),color);
                    var arrow = (track.Hit != null ? track.Hit.EnemyImpulse : Vector2.zero) * .06f; if (mirror) arrow.x = -arrow.x;
                    Vector2 from = World(box.center), to = World(box.center+arrow);
                    DrawLine(from,to,Coral);
                    if ((to-from).sqrMagnitude > 16)
                    {
                        Vector2 direction = (from-to).normalized * 8;
                        DrawLine(to,to+Rotate(direction,25),Coral); DrawLine(to,to+Rotate(direction,-25),Coral);
                    }
                    if (pixels.Overlaps(canvas)) HandleBoxDrag(pixels,corner,track);
                }
            Handles.color = original; Handles.EndGUI();
            GUI.Label(new Rect(canvas.x+10,canvas.y+8,canvas.width-20,21),"JOEY  →  DUMMY   |   " + PhaseName(),EditorStyles.whiteMiniLabel);
            if (Track != null && !Track.IsActive(frame)) GUI.Label(new Rect(canvas.x+10,canvas.yMax-24,canvas.width-20,20),"Ghost: esta hitbox no golpea en el frame seleccionado.",EditorStyles.whiteMiniLabel);
            GUI.EndGroup();
            canvas = layoutCanvas;
        }

        void DrawClipPreview()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (preview == null || previewAsset != Move.PreviewPrefab)
            {
                CleanupPreview(); preview = new PreviewRenderUtility(); previewAsset = Move.PreviewPrefab;
                previewInstance = preview.InstantiatePrefabInScene(previewAsset);
                previewInstance.hideFlags = HideFlags.HideAndDontSave;
                foreach (var behaviour in previewInstance.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
                preview.camera.orthographic = true; preview.camera.nearClipPlane = .01f; preview.camera.farClipPlane = 100;
                preview.camera.clearFlags = CameraClearFlags.SolidColor; preview.camera.backgroundColor = new Color(.045f,.065f,.09f);
                preview.ambientColor = Color.white * .65f; preview.lights[0].intensity = 1.2f;
            }
            AttackSampling.SampleVisual(Move,previewInstance,frame);
            Vector3 scale = Move.PreviewPrefab.transform.localScale; scale.x *= mirror ? -1 : 1; previewInstance.transform.localScale = scale;
            Vector2 centre = new Vector2((canvas.center.x-origin.x)/zoom,(origin.y-canvas.center.y)/zoom);
            preview.camera.transform.SetPositionAndRotation(new Vector3(centre.x,centre.y,-10),Quaternion.identity);
            preview.camera.orthographicSize = canvas.height/(zoom*2);
            preview.BeginPreview(canvas,GUIStyle.none);
            try { preview.Render(true); }
            finally { var image = preview.EndPreview(); GUI.DrawTexture(canvas,image,ScaleMode.StretchToFill,false); }
        }

        void CleanupPreview()
        {
            if (preview != null) preview.Cleanup();
            preview = null; previewInstance = previewAsset = null;
        }

        void HandleBoxDrag(Rect pixels, Vector2 corner, HitboxTrack track)
        {
            Rect handle = new Rect(corner-Vector2.one*7,Vector2.one*14);
            EditorGUIUtility.AddCursorRect(handle,MouseCursor.ResizeUpRight);
            EditorGUIUtility.AddCursorRect(pixels,MouseCursor.MoveArrow);
            var evt = Event.current;
            int control = GUIUtility.GetControlID(72183,FocusType.Passive,canvas);
            if (evt.type == EventType.MouseDown && evt.button == 0 && canvas.Contains(evt.mousePosition) && (pixels.Contains(evt.mousePosition) || handle.Contains(evt.mousePosition)))
            {
                dragUndoGroup=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Editar key de hitbox");Undo.RecordObject(draft,"Editar key de hitbox");
                dragKey = ExactKey(track,frame,true);
                dragStart = Local(evt.mousePosition); originalOffset = dragKey.Offset;
                resizing = handle.Contains(evt.mousePosition); dragging = true; dragControl = control; GUIUtility.hotControl = control;
                playing = false; MarkChanged(); evt.Use();
            }
            if (dragging && GUIUtility.hotControl == dragControl && evt.type == EventType.MouseDrag)
            {
                Undo.RecordObject(draft,"Editar key de hitbox");
                Vector2 current = Local(evt.mousePosition);
                if (resizing) dragKey.Size = Vector2.Max(Vector2.one * .04f,new Vector2(Mathf.Abs(current.x-dragKey.Offset.x),Mathf.Abs(current.y-dragKey.Offset.y))*2);
                else dragKey.Offset = originalOffset + current - dragStart;
                serialized.Update(); MarkChanged(); Repaint(); evt.Use();
            }
            if (dragging && evt.type == EventType.MouseUp)
            { Undo.CollapseUndoOperations(dragUndoGroup);dragging = false; dragKey = null; GUIUtility.hotControl = 0; evt.Use(); }
        }

        void DrawTimeline()
        {
            Rect area = GUILayoutUtility.GetRect(200,40 + Mathf.Min(5,Move.Hitboxes.Count)*20,GUILayout.ExpandWidth(true));
            Rect bar = new Rect(area.x+4,area.y+3,area.width-8,22);
            float total = Move.Total;
            DrawPhase(bar,0,Move.Startup,total,Coral,"Startup " + Move.Startup);
            DrawPhase(bar,Move.Startup,Move.Active,total,Gold,"Active " + Move.Active);
            DrawPhase(bar,Move.Startup+Move.Active,Move.Recovery,total,Violet,"Recovery " + Move.Recovery);
            for (int i=0;i<Mathf.Min(5,Move.Hitboxes.Count);i++)
            {
                var track=Move.Hitboxes[i]; if(track==null)continue;
                Rect row=new Rect(bar.x,bar.yMax+5+i*20,bar.width,17);
                EditorGUI.DrawRect(row,new Color(.12f,.15f,.18f));
                Rect active=new Rect(row.x+row.width*track.StartFrame/total,row.y,Mathf.Max(2,row.width*(track.EndFrame-track.StartFrame)/total),row.height);
                EditorGUI.DrawRect(active,i==trackIndex?Teal:new Color(.24f,.46f,.49f));
                GUI.Label(row,track.Name + "  [" + track.StartFrame + ", " + track.EndFrame + ")",EditorStyles.whiteMiniLabel);
                if (track.Keys != null) foreach(var key in track.Keys)
                { if(key==null)continue;float x=row.x+row.width*key.Frame/total;EditorGUI.DrawRect(new Rect(x-2,row.y+5,4,7),Color.white); }
                if(Event.current.type==EventType.MouseDown && row.Contains(Event.current.mousePosition))trackIndex=i;
            }
            float cursor=bar.x+(frame+.5f)/total*bar.width;
            EditorGUI.DrawRect(new Rect(cursor,bar.y,1,area.height-6),Color.white);
            if ((Event.current.type==EventType.MouseDown || Event.current.type==EventType.MouseDrag) && area.Contains(Event.current.mousePosition) && Event.current.button==0)
            { frame=Mathf.Clamp(Mathf.FloorToInt((Event.current.mousePosition.x-bar.x)/bar.width*total),0,Move.Total-1);playing=false;Event.current.Use();Repaint(); }
            if(Move.Recovery==0)GUI.Label(new Rect(bar.xMax-102,bar.y,100,22),"Recovery 0",EditorStyles.whiteMiniLabel);
        }

        static void DrawPhase(Rect bar,float start,float length,float total,Color color,string text)
        {
            if(length<=0)return;
            Rect segment=new Rect(bar.x+bar.width*start/total,bar.y,bar.width*length/total,bar.height);
            EditorGUI.DrawRect(segment,new Color(color.r*.65f,color.g*.65f,color.b*.65f));
            GUI.Label(segment,text,EditorStyles.whiteMiniLabel);
        }

        void DrawInspector()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox,GUILayout.Width(330),GUILayout.ExpandHeight(true)))
            {
                inspectorTab=GUILayout.Toolbar(inspectorTab,new[]{"Ataque","Bindings"});
                inspectorScroll=EditorGUILayout.BeginScrollView(inspectorScroll);
                EditorGUI.BeginChangeCheck();
                if(inspectorTab==0 && Move!=null) DrawMoveInspector(); else DrawBindingInspector();
                if(EditorGUI.EndChangeCheck())
                { ApplyPropertiesAndRetime();MarkChanged();ClampSelection(); }
                EditorGUILayout.EndScrollView();
                string error=Validate(draft);
                if(error!=null)EditorGUILayout.HelpBox(error,MessageType.Warning);
            }
        }

        SerializedProperty MoveProperty => serialized.FindProperty("Moves").GetArrayElementAtIndex(moveIndex);
        void Field(SerializedProperty parent,string name,string label=null,string tooltip=null,bool children=false)
        {
            var property=parent.FindPropertyRelative(name);
            if(property!=null)EditorGUILayout.PropertyField(property,new GUIContent(label??ObjectNames.NicifyVariableName(name),tooltip),children);
        }

        void DrawMoveInspector()
        {
            var property=MoveProperty;
            Field(property,"Id","ID único");Field(property,"Title","Nombre");Field(property,"Action","Acción");
            GUILayout.Label("Fases / duración a 60 Hz",EditorStyles.boldLabel);
            using(new EditorGUILayout.HorizontalScope())
                foreach(string timing in new[]{"Startup","Active","Recovery"})
                    using(new EditorGUILayout.VerticalScope())
                    {GUILayout.Label(timing,EditorStyles.miniLabel);EditorGUILayout.PropertyField(property.FindPropertyRelative(timing),GUIContent.none,GUILayout.MinWidth(40));}
            GUILayout.Label("Total: "+Move.Total+"f · "+(Move.Total/60f).ToString("0.###")+" s. Recovery puede ser 0.",tinyStyle);
            Field(property,"Finisher","Movimiento de cierre","Describe un remate; el cierre real depende de su lanzamiento y posicionamiento.");
            GUILayout.Space(6);GUILayout.Label("Inercia y movimiento propio",EditorStyles.boldLabel);
            Field(property,"MomentumRetention","Inercia horizontal","0.2 conserva el 20% de la velocidad previa, antes del impulso propio.");
            Field(property,"VerticalMomentumRetention","Inercia vertical");Field(property,"StartImpulseMode","Modo de impulso inicial");
            Field(property,"Impulse","Impulso al iniciar");Field(property,"GravityScale","Multiplicador de gravedad");
            Field(property,"BlinkToTarget","Reposicionar al objetivo");
            if(Move.BlinkToTarget)Field(property,"BlinkRange","Alcance de reposición");
            Field(property,"FollowOnHold","Ascender al mantener","El seguimiento se activa al mantener básico, aunque el ataque falle.");
            if(Move.FollowOnHold){Field(property,"FollowFrame","Frame de seguimiento");Field(property,"FollowImpulse","Impulso de seguimiento");}
            GUILayout.Space(6);GUILayout.Label("Animación",EditorStyles.boldLabel);
            Field(property,"AnimationClip","Animation Clip");Field(property,"PreviewPrefab","Prefab visual / preview");Field(property,"AnimationSpeed","Velocidad del clip");
            GUILayout.Label("Clip: frame / 60 × velocidad. El prefab usa su origen como pies; sin root motion. Sin clip se usan las poses del maniquí.",tinyStyle);
            DrawPoseInspector(property);
            GUILayout.Space(6);DrawTracksInspector(property);
            GUILayout.Space(6);GUILayout.Label("Cancels opcionales",EditorStyles.boldLabel);
            Field(property,"Cancels","Acciones permitidas","None deja terminar el movimiento. Los enlaces normales ocurren al terminar, según espacio y juggle.");
            if(Move.Cancels!=CancelActions.None)
            {Field(property,"CancelOpen","Abrir en frame");Field(property,"CancelClose","Cerrar en frame");Field(property,"CancelRequiresHit","Exigir impacto");}
            if(GUILayout.Button("Crear binding para este ataque"))AddBinding();
        }

        void DrawPoseInspector(SerializedProperty move)
        {
            var poses=move.FindPropertyRelative("Poses");
            int index=Move.Poses.FindIndex(p=>p!=null&&p.Frame==frame);
            if(index<0 && GUILayout.Button("+ Key de pose en frame "+frame))
            {
                Edit("Key de pose",()=>
                {
                    if(index>=0)return;
                    var sampled=Move.SamplePose(frame)??new PoseKeyframe();
                    sampled.Frame=frame;Move.Poses.Add(sampled);
                });
            }
            if(index>=0)
            {
                GUILayout.Label("Key de pose · frame "+frame,EditorStyles.miniBoldLabel);
                var key=poses.GetArrayElementAtIndex(index);
                Field(key,"BodyOffset","Posición del cuerpo");Field(key,"BodyAngle","Ángulo del cuerpo");
                Field(key,"FrontArmAngle","Brazo frontal");Field(key,"RearArmAngle","Brazo trasero");Field(key,"FrontLegAngle","Pierna frontal");Field(key,"RearLegAngle","Pierna trasera");
                if(GUILayout.Button("Quitar key de pose"))Edit("Quitar pose",()=>Move.Poses.RemoveAt(index));
            }
            EditorGUILayout.PropertyField(poses,new GUIContent("Todas las keys de pose"),true);
        }

        void DrawTracksInspector(SerializedProperty move)
        {
            GUILayout.Label("Hitboxes por ventana",EditorStyles.boldLabel);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("+ Hitbox"))Edit("Añadir hitbox",()=>
                {
                    var hit=new HitboxTrack{Name="Hitbox "+(Move.Hitboxes.Count+1),StartFrame=Move.Startup,EndFrame=Mathf.Min(Move.Total,Move.Startup+Mathf.Max(1,Move.Active))};
                    if(hit.EndFrame<=hit.StartFrame){hit.StartFrame=Mathf.Max(0,Move.Total-1);hit.EndFrame=Move.Total;}
                    hit.Keys.Add(new HitboxKeyframe{Frame=hit.StartFrame});Move.Hitboxes.Add(hit);trackIndex=Move.Hitboxes.Count-1;frame=hit.StartFrame;
                });
                using(new EditorGUI.DisabledScope(Track==null))
                    if(GUILayout.Button("Quitar"))Edit("Quitar hitbox",()=>{Move.Hitboxes.RemoveAt(trackIndex);trackIndex=Mathf.Max(0,trackIndex-1);});
            }
            if(Track==null)return;
            string[] names=Move.Hitboxes.Select((h,i)=>i+": "+(h?.Name??"vacía")).ToArray();
            trackIndex=EditorGUILayout.Popup("Seleccionada",trackIndex,names);
            var property=move.FindPropertyRelative("Hitboxes").GetArrayElementAtIndex(trackIndex);
            Field(property,"Name","Nombre");Field(property,"StartFrame","Inicio (incluido)");Field(property,"EndFrame","Fin (excluido)");
            Field(property,"HitGroup","Grupo de impacto","Las cajas del mismo grupo comparten contador: no duplican daño sobre el mismo objetivo.");
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("Ir al inicio")){frame=Track.StartFrame;playing=false;}
                using(new EditorGUI.DisabledScope(Move.Active<=0))
                    if(GUILayout.Button("Ajustar a Active"))Edit("Ajustar ventana",()=>RetimeTrack(Track,Move.Startup,Move.Startup+Move.Active));
            }
            int keyIndex=Track.Keys.FindIndex(k=>k!=null&&k.Frame==frame);
            if(keyIndex<0)
            {if(GUILayout.Button("+ Key de hitbox en frame "+frame))Edit("Añadir key de hitbox",()=>ExactKey(Track,frame,true));}
            else
            {
                var key=property.FindPropertyRelative("Keys").GetArrayElementAtIndex(keyIndex);
                Field(key,"Offset","Centro local");Field(key,"Size","Tamaño");
                using(new EditorGUI.DisabledScope(Track.Keys.Count<=1))
                    if(GUILayout.Button("Quitar key actual"))Edit("Quitar key de hitbox",()=>Track.Keys.RemoveAt(keyIndex));
            }
            EditorGUILayout.PropertyField(property.FindPropertyRelative("Keys"),new GUIContent("Todas las keys de hitbox"),true);
            var hit=property.FindPropertyRelative("Hit");
            GUILayout.Label("Propiedades del impacto",EditorStyles.boldLabel);
            Field(hit,"Damage","Daño");Field(hit,"EnemyImpulse","Impulso al enemigo");Field(hit,"EnemyVelocityMode","Velocidad del enemigo","Add suma a la velocidad actual; Override la reemplaza.");
            Field(hit,"AttackerImpulse","Impulso al jugador al golpear");Field(hit,"AttackerVelocityMode","Velocidad del jugador");
            Field(hit,"Hitstun","Hitstun (frames)");Field(hit,"Hitstop","Hitstop (frames)");Field(hit,"Rehit","Reimpacto (frames)","0: una vez por ejecución/grupo. Mayor a 0: intervalo mínimo entre impactos del mismo grupo.");
            Field(hit,"JugglePower","Potencia de juggle","El perfil enemigo compara este aporte con su umbral aéreo. Golpes rápidos pueden acumular juggle.");Field(hit,"ArmorDamage","Rotura de armadura");
            Field(hit,"Correction","Ayuda de separación","Sólo después de un impacto confirmado; no convierte fallos en aciertos.");
            if(Track.Hit.Correction)
            {
                Field(hit,"PreferredDistance","Distancia preferida");Field(hit,"MaxCorrection","Corrección máxima");Field(hit,"CorrectionSpeed","Velocidad de corrección");Field(hit,"PlayerCorrectionShare","Proporción al jugador","0.3 = 30% jugador / 70% enemigo ligero; el peso del enemigo modifica el reparto.");
            }
        }

        void DrawBindingInspector()
        {
            GUILayout.Label("Cómo se ejecutan los ataques",EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Un binding relaciona botón + suelo/aire + dirección + Rag/traje con una secuencia de IDs. Mayor Priority gana. Añadir IDs aquí no necesita cambiar código.",MessageType.Info);
            if(GUILayout.Button("+ Binding para el ataque seleccionado"))AddBinding();
            var bindings=serialized.FindProperty("Bindings");
            for(int i=0;i<draft.Bindings.Count;i++)
            {
                var binding=bindings.GetArrayElementAtIndex(i);
                using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using(new EditorGUILayout.HorizontalScope())
                    {
                        binding.isExpanded=EditorGUILayout.Foldout(binding.isExpanded,draft.Bindings[i]?.Name??"Binding",true);
                        if(GUILayout.Button("×",GUILayout.Width(24))){int remove=i;Edit("Quitar binding",()=>draft.Bindings.RemoveAt(remove));GUIUtility.ExitGUI();}
                    }
                    if(!binding.isExpanded)continue;
                    Field(binding,"Name","Nombre");Field(binding,"Action","Botón");Field(binding,"Context","Contexto");Field(binding,"Direction","Dirección");Field(binding,"Stance","Traje");Field(binding,"AnyRag","Cualquier Rag");
                    if(!draft.Bindings[i].AnyRag)Field(binding,"Rag","Rag");
                    Field(binding,"Priority","Prioridad");
                    var sequence=binding.FindPropertyRelative("Sequence");
                    GUILayout.Label("Secuencia por pulsaciones",EditorStyles.boldLabel);
                    for(int j=0;j<sequence.arraySize;j++)
                    {
                        using(new EditorGUILayout.HorizontalScope())
                        {
                            var id=sequence.GetArrayElementAtIndex(j);
                            string[] ids=draft.Moves.Where(m=>m!=null).Select(m=>m.Id).ToArray();
                            int current=Array.IndexOf(ids,id.stringValue);
                            int chosen=EditorGUILayout.Popup((j+1)+"",current,ids);
                            if(chosen>=0&&chosen<ids.Length)id.stringValue=ids[chosen];
                            if(GUILayout.Button("−",GUILayout.Width(24))){sequence.DeleteArrayElementAtIndex(j);break;}
                        }
                    }
                    if(GUILayout.Button("+ Paso de secuencia")){sequence.InsertArrayElementAtIndex(sequence.arraySize);sequence.GetArrayElementAtIndex(sequence.arraySize-1).stringValue=Move?.Id??draft.Moves.FirstOrDefault()?.Id??"";}
                    Field(binding,"SequenceTimeout","Timeout de secuencia (frames)");
                    Field(binding,"ChargedMoveId","ID de versión cargada");Field(binding,"ChargeFrames","Frames de carga");
                }
            }
        }

        void AddMove()
        {
            Edit("Crear ataque",()=>
            {
                string id=UniqueId("nuevo_ataque");
                var move=new MoveDefinition{Id=id,Title="Nuevo ataque",Startup=6,Active=3,Recovery=0,Cancels=CancelActions.None};
                var track=new HitboxTrack{StartFrame=6,EndFrame=9};track.Keys.Add(new HitboxKeyframe{Frame=6});move.Hitboxes.Add(track);
                move.Poses.Add(new PoseKeyframe{Frame=0,FrontArmAngle=-15});move.Poses.Add(new PoseKeyframe{Frame=6,FrontArmAngle=85,BodyAngle=-10});
                draft.Moves.Add(move);moveIndex=draft.Moves.Count-1;trackIndex=frame=0;search="";CleanupPreview();
            });
        }
        void DuplicateMove()
        {
            if(Move==null)return;
            Edit("Duplicar ataque",()=>{var copy=JsonUtility.FromJson<MoveDefinition>(JsonUtility.ToJson(Move));copy.Id=UniqueId(Move.Id+"_copia");copy.Title=Move.Title+" (copia)";draft.Moves.Add(copy);moveIndex=draft.Moves.Count-1;trackIndex=frame=0;search="";CleanupPreview();});
        }
        void DeleteMove()
        {
            if(Move==null)return;
            Edit("Eliminar ataque",()=>
            {
                string id=Move.Id;draft.Moves.RemoveAt(moveIndex);
                foreach(var binding in draft.Bindings){binding.Sequence.RemoveAll(x=>x==id);if(binding.ChargedMoveId==id)binding.ChargedMoveId="";}
                draft.Bindings.RemoveAll(b=>b.Sequence.Count==0);moveIndex=Mathf.Clamp(moveIndex,0,draft.Moves.Count-1);trackIndex=frame=0;CleanupPreview();
            });
        }
        void AddBinding()
        {
            if(Move==null)return;
            Edit("Crear binding",()=>{draft.Bindings.Add(new MoveBinding{Name=Move.Title,Action=Move.Action,Context=MoveContext.Ground,Direction=MoveDirection.Neutral,Stance=MoveStance.Normal,Priority=100,Sequence=new List<string>{Move.Id}});inspectorTab=1;});
        }
        string UniqueId(string seed)
        {string id=seed;int suffix=2;while(draft.Moves.Any(m=>m!=null&&m.Id==id))id=seed+"_"+suffix++;return id;}
        void Edit(string label,Action edit)
        {
            if(!draft)return;
            ApplyPropertiesAndRetime();Undo.RecordObject(draft,label);edit();serialized=new SerializedObject(draft);MarkChanged();ClampSelection();Repaint();
            if(Event.current!=null)GUIUtility.ExitGUI();
        }
        void ApplyPropertiesAndRetime()
        {
            if(serialized==null)return;
            var current=Move;int oldStartup=current?.Startup??0,oldActive=current?.Active??0;
            Undo.RecordObject(draft,"Editar ataque");
            serialized.ApplyModifiedProperties();
            if(current!=null&&(oldStartup!=current.Startup||oldActive!=current.Active))RetimePhases(current,oldStartup,oldActive);
            serialized.Update();
        }

        /// <summary>Retiming is explicit in the draft: changing startup shifts boxes/keys; active bounds clip their windows.</summary>
        public static void RetimePhases(MoveDefinition move,int previousStartup,int previousActive)
        {
            int delta=move.Startup-previousStartup;
            if(move.Hitboxes==null)return;
            foreach(var track in move.Hitboxes)
            {
                if(track==null)continue;
                if(delta!=0)
                {
                    track.StartFrame+=delta;track.EndFrame+=delta;
                    if(track.Keys!=null)foreach(var key in track.Keys)if(key!=null)key.Frame=Mathf.Max(0,key.Frame+delta);
                }
                if(move.Active>0)
                {
                    int first=move.Startup,last=move.Startup+move.Active;
                    int start=Mathf.Clamp(track.StartFrame,first,last-1),end=Mathf.Clamp(track.EndFrame,start+1,last);
                    if(start!=track.StartFrame||end!=track.EndFrame)RetimeTrack(track,start,end);
                }
                RemoveDuplicateKeys(track);
            }
        }
        static void RetimeTrack(HitboxTrack track,int start,int end)
        {
            int originalStart=track.StartFrame,oldSpan=Mathf.Max(1,track.EndFrame-track.StartFrame-1),newSpan=Mathf.Max(0,end-start-1);
            if(track.Keys!=null)foreach(var key in track.Keys)if(key!=null)
                key.Frame=Mathf.Clamp(start+Mathf.RoundToInt((key.Frame-originalStart)/(float)oldSpan*newSpan),start,Mathf.Max(start,end-1));
            track.StartFrame=start;track.EndFrame=end;RemoveDuplicateKeys(track);
        }
        static void RemoveDuplicateKeys(HitboxTrack track)
        {
            if(track.Keys==null)return;
            track.Keys=track.Keys.Where(k=>k!=null).GroupBy(k=>k.Frame).Select(group=>group.Last()).OrderBy(k=>k.Frame).ToList();
        }
        void MarkChanged(){observedDraftJson=JsonUtility.ToJson(draft);hasUnsavedChanges=observedDraftJson!=savedDraftJson;Repaint();}
        void ClampSelection()
        {
            if(!draft)return;moveIndex=Mathf.Clamp(moveIndex,0,Mathf.Max(0,draft.Moves.Count-1));
            if(Move==null)return;
            Move.Hitboxes=Move.Hitboxes??new List<HitboxTrack>();Move.Poses=Move.Poses??new List<PoseKeyframe>();
            trackIndex=Mathf.Clamp(trackIndex,0,Mathf.Max(0,Move.Hitboxes.Count-1));frame=Mathf.Clamp(frame,0,Move.Total-1);
            if(Track!=null){Track.Keys=Track.Keys??new List<HitboxKeyframe>();Track.Hit=Track.Hit??new HitProperties();}
        }
        void HandleShortcuts()
        {
            var evt=Event.current;
            if(evt.type!=EventType.KeyDown)return;
            if((evt.control||evt.command)&&evt.keyCode==KeyCode.S){SaveDraft();evt.Use();return;}
            if(EditorGUIUtility.editingTextField||Move==null)return;
            if(evt.keyCode==KeyCode.Space){playing=!playing;evt.Use();}
            if(evt.keyCode==KeyCode.LeftArrow){playing=false;frame=Mathf.Max(0,frame-1);evt.Use();}
            if(evt.keyCode==KeyCode.RightArrow){playing=false;frame=Mathf.Min(Move.Total-1,frame+1);evt.Use();}
        }
        static HitboxKeyframe ExactKey(HitboxTrack track,int at,bool create)
        {
            var key=track.Keys.Find(k=>k!=null&&k.Frame==at);if(key!=null||!create)return key;
            Rect sample=track.Sample(at);key=new HitboxKeyframe{Frame=at,Offset=sample.center,Size=sample.size};track.Keys.Add(key);return key;
        }
        string PhaseName()=>frame<Move.Startup?"STARTUP":frame<Move.Startup+Move.Active?"ACTIVE":"RECOVERY";
        Vector2 World(Vector2 point)=>new Vector2(origin.x+point.x*zoom,origin.y-point.y*zoom);
        Vector2 Local(Vector2 point)=>new Vector2((point.x-origin.x)/zoom*(mirror?-1:1),(origin.y-point.y)/zoom);
        Rect WorldRect(Rect r){Vector2 a=World(new Vector2(r.xMin,r.yMax));return new Rect(a,r.size*zoom);}
        void DrawBox(Rect r,Color color,bool fill)
        {
            Rect pixels=WorldRect(r);
            if(fill)EditorGUI.DrawRect(Clip(pixels,canvas),new Color(color.r,color.g,color.b,.09f));
            DrawLine(new Vector2(pixels.x,pixels.y),new Vector2(pixels.xMax,pixels.y),color);DrawLine(new Vector2(pixels.xMax,pixels.y),new Vector2(pixels.xMax,pixels.yMax),color);
            DrawLine(new Vector2(pixels.xMax,pixels.yMax),new Vector2(pixels.x,pixels.yMax),color);DrawLine(new Vector2(pixels.x,pixels.yMax),new Vector2(pixels.x,pixels.y),color);
        }
        static Rect Clip(Rect a,Rect b)=>Rect.MinMaxRect(Mathf.Max(a.xMin,b.xMin),Mathf.Max(a.yMin,b.yMin),Mathf.Max(Mathf.Max(a.xMin,b.xMin),Mathf.Min(a.xMax,b.xMax)),Mathf.Max(Mathf.Max(a.yMin,b.yMin),Mathf.Min(a.yMax,b.yMax)));
        static Vector2 Rotate(Vector2 value,float angle){float s=Mathf.Sin(angle*Mathf.Deg2Rad),c=Mathf.Cos(angle*Mathf.Deg2Rad);return new Vector2(value.x*c-value.y*s,value.x*s+value.y*c);}
        void DrawLine(Vector2 a,Vector2 b,Color color)
        {Handles.color=color;Handles.DrawAAPolyLine(1.5f,a,b);}
        void DrawPuppet(Vector2 feet,PoseKeyframe pose,Color color,bool flip)
        {
            pose=pose??new PoseKeyframe();float sign=flip?-1:1;
            Func<Vector2,Vector2> point=p=>feet+new Vector2((p.x+pose.BodyOffset.x)*sign,p.y+pose.BodyOffset.y);
            Func<Vector2,float,Vector2> limb=(direction,angle)=>Rotate(direction,angle+pose.BodyAngle);
            Vector2 hip=point(Rotate(new Vector2(0,.62f),pose.BodyAngle)),shoulder=point(Rotate(new Vector2(0,1.15f),pose.BodyAngle));
            Handles.color=color;
            Handles.DrawAAPolyLine(9,World(hip),World(shoulder));
            Handles.DrawSolidDisc(World(point(Rotate(new Vector2(0,1.48f),pose.BodyAngle))),Vector3.forward,.22f*zoom);
            Handles.DrawAAPolyLine(5,World(shoulder),World(shoulder+new Vector2(limb(new Vector2(.15f,-.48f),pose.RearArmAngle).x*sign,limb(new Vector2(.15f,-.48f),pose.RearArmAngle).y)));
            Handles.DrawAAPolyLine(6,World(shoulder),World(shoulder+new Vector2(limb(new Vector2(.25f,-.5f),pose.FrontArmAngle).x*sign,limb(new Vector2(.25f,-.5f),pose.FrontArmAngle).y)));
            foreach(float angle in new[]{pose.FrontLegAngle-15,pose.RearLegAngle+15})
            {Vector2 leg=limb(new Vector2(0,-.62f),angle);leg.x*=sign;Handles.DrawAAPolyLine(6,World(hip),World(hip+leg));}
        }
    }

    [CustomEditor(typeof(CombatCatalog))]
    public sealed class CombatCatalogInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if(GUILayout.Button("Abrir Taller de ataques",GUILayout.Height(32)))AttackWorkshop.Open((CombatCatalog)target);
            EditorGUILayout.HelpBox("El Taller ofrece timeline, hitboxes arrastrables, preview de animación y bindings. Aquí se mantiene el inspector serializado para revisión avanzada.",MessageType.Info);
            DrawDefaultInspector();
        }
    }
}
