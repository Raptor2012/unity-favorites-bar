using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FavoritesBar
{
    /// <summary>
    /// Favorites bar. Hold Alt (see Preferences > Favorites Bar) over the Project window to show it
    /// on top of that window; release to hide, or lock it. Window > Favorites Bar opens a dockable copy.
    /// Pages: click a tab, scroll, 1-9 or Left/Right. Items: click to select, double-click or Enter to open,
    /// drag to reorder or out into the scene, inspector fields and other windows.
    /// </summary>
    public class FavoritesWindow : EditorWindow
    {
        const float RowHeight = 22f, HeaderHeight = 22f;
        const float IntroSeconds = 0.16f, RowStagger = 0.012f, Glide = 28f;
        const string DragKey = "FavoritesBar.Row";

        static readonly Color Accent = new Color(0.24f, 0.49f, 0.91f);
        static FavoritesWindow overlay;
        static bool opening;
        static GUIContent lockIcon, addIcon, pageIcon, sceneObjectIcon;

        struct RowLabel
        {
            public GUIContent content;
            public bool missing;
        }

        bool isOverlay, pinned, renaming;
        int cursor = -1, pressed = -1, dropIndex = -1, hovered = -1;
        Vector2 pressPosition, scroll;
        readonly Dictionary<string, Object> resolved = new Dictionary<string, Object>();
        readonly Dictionary<string, RowLabel> labels = new Dictionary<string, RowLabel>();

        // Animation: eased intros on open and page change, highlights that glide to their target.
        double lastRepaint, introStart;
        float introDirection, dt, cursorY, hoverY, hoverAlpha, dropY, tabX, tabWidth, tabTargetX, tabTargetWidth;
        bool animating;

        static FavoritesStore Store => FavoritesStore.instance;
        static FavoritesStore.Page Page => Store.CurrentPage;
        static double Now => EditorApplication.timeSinceStartup;

        [MenuItem("Window/Favorites Bar")]
        static void OpenDocked() => GetWindow<FavoritesWindow>("Favorites");

        [InitializeOnLoadMethod]
        static void Hook()
        {
            EditorApplication.modifierKeysChanged += () =>
            {
                if (overlay) overlay.Repaint();
                else if (IsProjectWindow(mouseOverWindow)) mouseOverWindow.Repaint();
            };
            EditorApplication.projectWindowItemOnGUI += (_, __) => TryShowOverlay();
        }

        static bool IsProjectWindow(EditorWindow window) => window && window.GetType().Name == "ProjectBrowser";

        // Runs for every item the Project window draws, so the cheap checks come first.
        static void TryShowOverlay()
        {
            if (overlay || opening || !FavoritesPrefs.Held(Event.current) || !FavoritesPrefs.Enabled) return;
            var project = mouseOverWindow;
            if (!IsProjectWindow(project)) return;

            opening = true;
            var area = project.position;
            EditorApplication.delayCall += () =>
            {
                opening = false;
                overlay = CreateInstance<FavoritesWindow>();
                overlay.isOverlay = true;
                overlay.position = area;
                overlay.ShowPopup();
                overlay.Focus();
            };
        }

        void OnEnable()
        {
            wantsMouseMove = true;
            wantsMouseEnterLeaveWindow = true;
            StartIntro(-1f);
        }

        void OnDisable() { if (overlay == this) overlay = null; }
        void OnLostFocus() { if (isOverlay && !pinned) Close(); }
        void OnHierarchyChange() => Invalidate();
        void OnProjectChange() => Invalidate();

        void Invalidate()
        {
            resolved.Clear();
            labels.Clear();
            Repaint();
        }

        void OnGUI()
        {
            var e = Event.current;
            if (isOverlay && !pinned && !renaming && e.type != EventType.Layout && !FavoritesPrefs.Held(e))
            {
                Close();
                return;
            }

            if (e.type == EventType.Repaint)
            {
                dt = Mathf.Clamp((float)(Now - lastRepaint), 0f, 0.05f);
                lastRepaint = Now;
                animating = false;
            }
            else if (e.type == EventType.MouseLeaveWindow) SetHovered(-1);

            DrawHeader(new Rect(0, 0, position.width, HeaderHeight));
            DrawItems(new Rect(0, HeaderHeight, position.width, position.height - HeaderHeight));
            HandleKeys(e);

            if (e.type != EventType.Repaint) return;
            if (isOverlay) EditorGUI.DrawRect(new Rect(0, 0, position.width, 1), EditorGUIUtility.isProSkin ? Color.black : Color.gray);
            if (animating) Repaint();
        }

        // ---- Animation ----

        void StartIntro(float direction)
        {
            introStart = Now;
            introDirection = direction;
        }

        /// <summary>0 → 1 ease-out progress of a row's intro; rows enter one after another.</summary>
        float Intro(int row)
        {
            float t = Mathf.Clamp01((float)(Now - introStart - row * RowStagger) / IntroSeconds);
            if (t < 1f) animating = true;
            return 1f - (1f - t) * (1f - t) * (1f - t);
        }

        /// <summary>Frame-rate independent glide towards a target; only advances on Repaint.</summary>
        float Approach(float value, float target)
        {
            if (Event.current.type != EventType.Repaint) return value;
            if (Mathf.Abs(value - target) < 0.01f) return target;
            animating = true;
            return Mathf.Lerp(value, target, 1f - Mathf.Exp(-dt * Glide));
        }

        void SetHovered(int index)
        {
            if (index == hovered) return;
            if (hovered < 0) hoverY = index * RowHeight; // appear in place instead of gliding in from far away
            hovered = index;
            Repaint();
        }

        static GUIContent Icon(ref GUIContent cache, string name, string tooltip) =>
            cache ??= new GUIContent(EditorGUIUtility.IconContent(name).image, tooltip);

        // ---- Pages ----

        void DrawHeader(Rect area)
        {
            var e = Event.current;
            GUI.Box(area, GUIContent.none, EditorStyles.toolbar);

            float right = area.xMax;
            if (isOverlay) pinned = GUI.Toggle(Slot(ref right, 24), pinned, Icon(ref lockIcon, "IN LockButton", "Keep open"), EditorStyles.toolbarButton);
            if (GUI.Button(Slot(ref right, 24), Icon(ref addIcon, "Toolbar Plus More", "Add the selection to this page"), EditorStyles.toolbarButton))
                Add(Selection.objects, Page.items.Count);
            if (GUI.Button(Slot(ref right, 24), Icon(ref pageIcon, "CreateAddNew", "New page"), EditorStyles.toolbarButton))
            {
                Store.pages.Add(new FavoritesStore.Page { name = "Page " + (Store.pages.Count + 1) });
                SetPage(Store.pages.Count - 1);
                renaming = true;
            }

            var current = Page; // creates the first page on first use
            float x = area.x;
            for (int i = 0; i < Store.pages.Count; i++)
            {
                var page = Store.pages[i];
                bool active = page == current;
                var label = new GUIContent(page.name, i < 9 ? $"Page {i + 1} (key {i + 1})" : null);
                var tab = new Rect(x, area.y, Mathf.Clamp(EditorStyles.toolbarButton.CalcSize(label).x + 8, 40, 160), area.height);
                if (renaming && active) tab.width = Mathf.Max(tab.width, 100);
                if (tab.xMax > right) break;
                x = tab.xMax;
                if (active)
                {
                    tabTargetX = tab.x;
                    tabTargetWidth = tab.width;
                }

                if (renaming && active)
                {
                    if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape))
                    {
                        renaming = false;
                        Store.Save();
                        e.Use();
                    }
                    GUI.SetNextControlName("FavoritesBar.Rename");
                    page.name = EditorGUI.TextField(new Rect(tab.x, tab.y + 2, tab.width, tab.height - 3), page.name);
                    if (renaming) EditorGUI.FocusTextInControl("FavoritesBar.Rename");
                    continue;
                }

                if (tab.Contains(e.mousePosition))
                {
                    if (e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2 && active) { renaming = true; e.Use(); }
                    else if (e.type == EventType.ContextClick) { PageMenu(i); e.Use(); }
                    else if (e.type == EventType.DragUpdated && !active) SetPage(i); // hover a tab while dragging to drop on that page
                }
                if (GUI.Toggle(tab, active, label, EditorStyles.toolbarButton) && !active) SetPage(i);
            }

            // Active-page indicator that slides between tabs.
            if (tabWidth <= 0f) { tabX = tabTargetX; tabWidth = tabTargetWidth; }
            tabX = Approach(tabX, tabTargetX);
            tabWidth = Approach(tabWidth, tabTargetWidth);
            if (e.type == EventType.Repaint) EditorGUI.DrawRect(new Rect(tabX + 2, area.yMax - 2, tabWidth - 4, 2), Accent);
        }

        static Rect Slot(ref float right, float width)
        {
            right -= width;
            return new Rect(right, 0, width, HeaderHeight);
        }

        void SetPage(int index)
        {
            if (Store.pages.Count == 0) return;
            int target = (index % Store.pages.Count + Store.pages.Count) % Store.pages.Count;
            if (target != Store.current) StartIntro(index > Store.current ? 1f : -1f);
            Store.current = target;
            Store.Save();
            cursor = -1;
            renaming = false;
            scroll = Vector2.zero;
            Repaint();
        }

        void PageMenu(int index)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Rename"), false, () => { SetPage(index); renaming = true; });
            if (Store.pages.Count > 1)
                menu.AddItem(new GUIContent("Delete Page"), false, () =>
                {
                    var page = Store.pages[index];
                    if (page.items.Count > 0 && !EditorUtility.DisplayDialog("Delete page", $"Delete \"{page.name}\" and its {page.items.Count} favorites?", "Delete", "Cancel")) return;
                    Store.pages.RemoveAt(index);
                    SetPage(Mathf.Min(Store.current, Store.pages.Count - 1));
                });
            menu.ShowAsContext();
        }

        // ---- Items ----

        void DrawItems(Rect area)
        {
            var e = Event.current;
            var items = Page.items;
            bool overflow = items.Count * RowHeight > area.height;
            var content = new Rect(0, 0, area.width - (overflow ? 14 : 0), Mathf.Max(items.Count * RowHeight, area.height));

            if (e.type == EventType.ScrollWheel && area.Contains(e.mousePosition) && !overflow)
            {
                SetPage(Store.current + (e.delta.y > 0 ? 1 : -1));
                e.Use();
            }
            bool mouseInList = area.Contains(e.mousePosition);

            scroll = GUI.BeginScrollView(area, scroll, content);

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag || e.type == EventType.DragUpdated)
            {
                int row = Mathf.FloorToInt(e.mousePosition.y / RowHeight);
                SetHovered(mouseInList && row >= 0 && row < items.Count ? row : -1);
            }
            DrawHighlights(content.width, items.Count);

            // Only the visible rows are drawn.
            int first = Mathf.Max(0, Mathf.FloorToInt(scroll.y / RowHeight));
            int last = Mathf.Min(items.Count, Mathf.CeilToInt((scroll.y + area.height) / RowHeight) + 1);
            for (int i = first; i < last; i++) DrawRow(new Rect(0, i * RowHeight, content.width, RowHeight), i);

            if (items.Count == 0)
            {
                GUI.color = new Color(1, 1, 1, Intro(0));
                GUI.Label(content, "Drag assets, folders or scene objects here\nor press + to add the selection", EditorStyles.centeredGreyMiniLabel);
                GUI.color = Color.white;
            }
            HandleDrop(content);
            GUI.EndScrollView();
        }

        void DrawHighlights(float width, int count)
        {
            var e = Event.current;
            hoverAlpha = Approach(hoverAlpha, hovered >= 0 && hovered != cursor ? 1f : 0f);
            if (hovered >= 0) hoverY = Approach(hoverY, hovered * RowHeight);
            if (cursor >= 0 && cursor < count) cursorY = Approach(cursorY, cursor * RowHeight);
            if (dropIndex >= 0) dropY = Approach(dropY, dropIndex * RowHeight);
            if (e.type != EventType.Repaint) return;

            if (hoverAlpha > 0.01f) EditorGUI.DrawRect(new Rect(0, hoverY, width, RowHeight), new Color(1, 1, 1, 0.07f * hoverAlpha));
            if (cursor >= 0 && cursor < count)
            {
                var row = new Rect(0, cursorY, width, RowHeight);
                EditorGUI.DrawRect(row, new Color(Accent.r, Accent.g, Accent.b, 0.3f));
                EditorGUI.DrawRect(new Rect(row.x, row.y + 3, 2, row.height - 6), Accent);
            }
            if (dropIndex >= 0) EditorGUI.DrawRect(new Rect(4, dropY - 1, width - 8, 2), Accent);
        }

        void DrawRow(Rect row, int index)
        {
            var e = Event.current;
            var item = Page.items[index];
            var label = Label(item);

            if (e.type == EventType.Repaint)
            {
                float k = Intro(index);
                var color = GUI.color;
                GUI.color = new Color(1, 1, 1, k * (label.missing ? 0.45f : 1f));
                GUI.Label(new Rect(row.x + 6 + (1f - k) * 16f * introDirection, row.y, row.width - 6, row.height), label.content);
                GUI.color = color;
                return;
            }

            if (!row.Contains(e.mousePosition)) return;
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0:
                    if (cursor < 0) cursorY = index * RowHeight;
                    cursor = pressed = index;
                    pressPosition = e.mousePosition;
                    if (e.clickCount == 2) Open(item, true);
                    e.Use();
                    break;
                case EventType.MouseDrag when pressed == index && Vector2.Distance(pressPosition, e.mousePosition) > 4:
                    pressed = -1;
                    DragAndDrop.PrepareStartDrag();
                    var target = Resolve(item);
                    DragAndDrop.objectReferences = target ? new[] { target } : new Object[0];
                    DragAndDrop.SetGenericData(DragKey, item);
                    DragAndDrop.StartDrag(label.content.text);
                    e.Use();
                    break;
                case EventType.MouseUp when pressed == index:
                    pressed = -1;
                    if (e.clickCount < 2) Open(item, false);
                    e.Use();
                    break;
                case EventType.ContextClick:
                    cursor = index;
                    ItemMenu(item);
                    e.Use();
                    break;
            }
        }

        void ItemMenu(FavoritesStore.Item item)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Open"), false, () => Open(item, true));
            menu.AddItem(new GUIContent("Select"), false, () => Open(item, false));
            menu.AddSeparator("");
            for (int i = 0; i < Store.pages.Count; i++)
            {
                if (i == Store.current) continue;
                var page = Store.pages[i];
                menu.AddItem(new GUIContent($"Move To/{i + 1}  {page.name}"), false, () =>
                {
                    Store.CurrentPage.items.Remove(item);
                    page.Insert(item, page.items.Count);
                    Store.Save();
                });
            }
            menu.AddItem(new GUIContent("Remove"), false, () => Remove(item));
            menu.ShowAsContext();
        }

        void HandleDrop(Rect content)
        {
            var e = Event.current;
            if (e.type == EventType.DragExited) { dropIndex = -1; Repaint(); return; }
            if ((e.type != EventType.DragUpdated && e.type != EventType.DragPerform) || !content.Contains(e.mousePosition)) return;

            var moved = DragAndDrop.GetGenericData(DragKey) as FavoritesStore.Item;
            if (moved == null && DragAndDrop.objectReferences.Length == 0) return;

            DragAndDrop.visualMode = moved != null ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Link;
            int index = Mathf.Clamp(Mathf.RoundToInt(e.mousePosition.y / RowHeight), 0, Page.items.Count);
            if (dropIndex < 0) dropY = index * RowHeight;
            dropIndex = index;

            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                if (moved != null)
                {
                    foreach (var page in Store.pages) if (page != Page) page.items.Remove(moved);
                    Page.Insert(moved, dropIndex);
                    Store.Save();
                }
                else Add(DragAndDrop.objectReferences, dropIndex);
                dropIndex = -1;
            }
            e.Use();
            Repaint();
        }

        void HandleKeys(Event e)
        {
            if (e.type != EventType.KeyDown || renaming) return;
            int count = Page.items.Count;
            var key = e.keyCode;
            if (key >= KeyCode.Alpha1 && key <= KeyCode.Alpha9 && key - KeyCode.Alpha1 < Store.pages.Count) SetPage(key - KeyCode.Alpha1);
            else if (key == KeyCode.LeftArrow) SetPage(Store.current - 1);
            else if (key == KeyCode.RightArrow) SetPage(Store.current + 1);
            else if ((key == KeyCode.DownArrow || key == KeyCode.UpArrow) && count > 0)
            {
                if (cursor < 0) cursorY = (key == KeyCode.DownArrow ? 0 : count - 1) * RowHeight;
                cursor = cursor < 0 ? (key == KeyCode.DownArrow ? 0 : count - 1) : Mathf.Clamp(cursor + (key == KeyCode.DownArrow ? 1 : -1), 0, count - 1);
            }
            else if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && cursor >= 0 && cursor < count) Open(Page.items[cursor], true);
            else if ((key == KeyCode.Delete || key == KeyCode.Backspace) && cursor >= 0 && cursor < count) Remove(Page.items[cursor]);
            else if (key == KeyCode.Escape && isOverlay) Close();
            else return;
            e.Use();
            Repaint();
        }

        // ---- Favorites ----

        void Add(IEnumerable<Object> objects, int index)
        {
            bool skipped = false;
            foreach (var obj in objects)
            {
                if (!obj) continue;
                var id = GlobalObjectId.GetGlobalObjectIdSlow(obj);
                if (id.identifierType == 0 || id.assetGUID.Empty()) { skipped = true; continue; } // unsaved scene, prefab stage, ...
                Page.Insert(new FavoritesStore.Item { id = id.ToString(), name = obj.name, subAsset = IsAsset(id) && !AssetDatabase.IsMainAsset(obj) }, index++);
            }
            Store.Save();
            if (skipped) ShowNotification(new GUIContent("Save the scene first to add its objects"));
        }

        void Remove(FavoritesStore.Item item)
        {
            Page.items.Remove(item);
            cursor = Mathf.Min(cursor, Page.items.Count - 1);
            Store.Save();
        }

        static bool IsAsset(GlobalObjectId id) => id.identifierType != 2;

        /// <summary>Row label and icon, cached until the project or hierarchy changes.</summary>
        RowLabel Label(FavoritesStore.Item item)
        {
            if (labels.TryGetValue(item.id, out var label)) return label;
            label = new RowLabel { content = new GUIContent(item.name), missing = true };
            if (GlobalObjectId.TryParse(item.id, out var id))
            {
                var path = AssetDatabase.GUIDToAssetPath(id.assetGUID);
                label.missing = string.IsNullOrEmpty(path);
                if (!label.missing && IsAsset(id))
                {
                    if (!item.subAsset) label.content.text = Path.GetFileNameWithoutExtension(path);
                    label.content.image = AssetDatabase.GetCachedIcon(path);
                    label.content.tooltip = path;
                }
                else if (!label.missing)
                {
                    var obj = Resolve(item);
                    if (obj) label.content.text = item.name = obj.name;
                    label.content.image = obj ? AssetPreview.GetMiniThumbnail(obj) : (sceneObjectIcon ??= EditorGUIUtility.IconContent("GameObject Icon")).image;
                    label.content.tooltip = $"{Path.GetFileNameWithoutExtension(path)} scene";
                }
            }
            labels[item.id] = label;
            return label;
        }

        /// <summary>The favorite's object; scene objects resolve only while their scene is open (cached until the hierarchy changes).</summary>
        Object Resolve(FavoritesStore.Item item)
        {
            if (resolved.TryGetValue(item.id, out var obj)) return obj;
            obj = GlobalObjectId.TryParse(item.id, out var id) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) : null;
            if (!IsAsset(id)) resolved[item.id] = obj; // assets load on demand, no need to cache
            return obj;
        }

        void Open(FavoritesStore.Item item, bool activate)
        {
            var obj = Resolve(item);
            if (!obj && GlobalObjectId.TryParse(item.id, out var id) && !IsAsset(id) && OpenScene(AssetDatabase.GUIDToAssetPath(id.assetGUID)))
            {
                Invalidate();
                obj = Resolve(item);
            }
            if (!obj) return;

            if (AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(obj)))
            {
                ShowFolder(obj);
                return;
            }
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
            if (!activate) return;
            if (obj is GameObject go && go.scene.IsValid()) SceneView.FrameLastActiveSceneView();
            else AssetDatabase.OpenAsset(obj);
        }

        static bool OpenScene(string path)
        {
            if (string.IsNullOrEmpty(path) || EditorApplication.isPlaying) return false;
            if (SceneManager.GetSceneByPath(path).isLoaded) return true;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            EditorSceneManager.OpenScene(path);
            return true;
        }

        static MethodInfo showFolderContents;
        static FieldInfo lastProjectBrowser;

        /// <summary>Shows the folder's contents in the Project window (two-column layout); falls back to pinging it.</summary>
        static void ShowFolder(Object folder)
        {
            if (showFolderContents == null)
            {
                var browserType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser");
                lastProjectBrowser = browserType?.GetField("s_LastInteractedProjectBrowser", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                showFolderContents = browserType?.GetMethod("ShowFolderContents", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(int), typeof(bool) }, null);
            }
            var browser = lastProjectBrowser?.GetValue(null);
            if (browser != null && showFolderContents != null) showFolderContents.Invoke(browser, new object[] { folder.GetInstanceID(), true });
            else
            {
                Selection.activeObject = folder;
                EditorGUIUtility.PingObject(folder);
            }
        }
    }
}
