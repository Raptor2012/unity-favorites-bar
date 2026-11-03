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
    /// on top of that window; release to hide, or pin it. Window > Favorites Bar opens a dockable copy.
    /// Pages: click a tab, scroll, 1-9 or Left/Right. Items: click to select, double-click or Enter to open,
    /// drag to reorder or out into the scene, inspector fields and other windows.
    /// </summary>
    public class FavoritesWindow : EditorWindow
    {
        const float RowHeight = 22f, HeaderHeight = 22f;
        const string DragKey = "FavoritesBar.Row";

        static FavoritesWindow overlay;
        static bool opening;

        bool isOverlay, pinned, renaming;
        int cursor = -1, pressed = -1, dropIndex = -1;
        Vector2 pressPosition, scroll;
        readonly Dictionary<string, Object> resolved = new Dictionary<string, Object>();

        static FavoritesStore Store => FavoritesStore.instance;
        static FavoritesStore.Page Page => Store.CurrentPage;

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

        static void TryShowOverlay()
        {
            if (overlay || opening || !FavoritesPrefs.Enabled || !FavoritesPrefs.Held(Event.current)) return;
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

        void OnEnable() => wantsMouseMove = true;
        void OnDisable() { if (overlay == this) overlay = null; }
        void OnLostFocus() { if (isOverlay && !pinned) Close(); }
        void OnHierarchyChange() { resolved.Clear(); Repaint(); }
        void OnProjectChange() { resolved.Clear(); Repaint(); }

        void OnGUI()
        {
            var e = Event.current;
            if (isOverlay && !pinned && !renaming && e.type != EventType.Layout && !FavoritesPrefs.Held(e))
            {
                Close();
                return;
            }

            DrawHeader(new Rect(0, 0, position.width, HeaderHeight));
            DrawItems(new Rect(0, HeaderHeight, position.width, position.height - HeaderHeight));
            HandleKeys(e);
            if (isOverlay && e.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(0, 0, position.width, 1), EditorGUIUtility.isProSkin ? Color.black : Color.gray);
        }

        // ---- Pages ----

        void DrawHeader(Rect area)
        {
            var e = Event.current;
            GUI.Box(area, GUIContent.none, EditorStyles.toolbar);

            float right = area.xMax;
            if (isOverlay) pinned = GUI.Toggle(Slot(ref right, 24), pinned, Icon("IN LockButton", "Keep open"), EditorStyles.toolbarButton);
            if (GUI.Button(Slot(ref right, 24), Icon("Toolbar Plus More", "Add the selection to this page"), EditorStyles.toolbarButton))
                Add(Selection.objects, Page.items.Count);
            if (GUI.Button(Slot(ref right, 24), Icon("CreateAddNew", "New page"), EditorStyles.toolbarButton))
            {
                Store.pages.Add(new FavoritesStore.Page { name = "Page " + (Store.pages.Count + 1) });
                SetPage(Store.pages.Count - 1);
                renaming = true;
            }

            float x = area.x;
            var current = Page; // creates the first page on first use
            for (int i = 0; i < Store.pages.Count; i++)
            {
                var page = Store.pages[i];
                var label = new GUIContent(page.name, i < 9 ? $"Page {i + 1} (key {i + 1})" : null);
                var tab = new Rect(x, area.y, Mathf.Clamp(EditorStyles.toolbarButton.CalcSize(label).x + 8, 40, 160), area.height);
                if (tab.xMax > right) break;
                x = tab.xMax;

                if (renaming && i == Store.current)
                {
                    if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape))
                    {
                        renaming = false;
                        Store.Save();
                        e.Use();
                    }
                    GUI.SetNextControlName("FavoritesBar.Rename");
                    page.name = EditorGUI.TextField(new Rect(tab.x, tab.y + 2, Mathf.Max(tab.width, 100), tab.height - 3), page.name);
                    if (renaming) EditorGUI.FocusTextInControl("FavoritesBar.Rename");
                    x = tab.x + Mathf.Max(tab.width, 100);
                    continue;
                }

                if (tab.Contains(e.mousePosition))
                {
                    if (e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2 && i == Store.current) { renaming = true; e.Use(); }
                    else if (e.type == EventType.ContextClick) { PageMenu(i); e.Use(); }
                    else if (e.type == EventType.DragUpdated && i != Store.current) SetPage(i); // drag over a tab to drop on that page
                }
                if (GUI.Toggle(tab, i == Store.current, label, EditorStyles.toolbarButton) && i != Store.current) SetPage(i);
            }
        }

        static Rect Slot(ref float right, float width)
        {
            right -= width;
            return new Rect(right, 0, width, HeaderHeight);
        }

        static GUIContent Icon(string name, string tooltip) => new GUIContent(EditorGUIUtility.IconContent(name).image, tooltip);

        void SetPage(int index)
        {
            if (Store.pages.Count == 0) return;
            Store.current = (index + Store.pages.Count) % Store.pages.Count;
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

            scroll = GUI.BeginScrollView(area, scroll, content);
            for (int i = 0; i < items.Count; i++) DrawRow(new Rect(0, i * RowHeight, content.width, RowHeight), i);
            if (items.Count == 0)
                GUI.Label(content, "Drag assets, folders or scene objects here\nor press + to add the selection", EditorStyles.centeredGreyMiniLabel);
            HandleDrop(content);
            GUI.EndScrollView();
        }

        void DrawRow(Rect row, int index)
        {
            var e = Event.current;
            var item = Page.items[index];

            if (e.type == EventType.Repaint)
            {
                if (index == cursor) EditorGUI.DrawRect(row, new Color(0.24f, 0.49f, 0.91f, 0.35f));
                else if (row.Contains(e.mousePosition)) EditorGUI.DrawRect(row, new Color(1, 1, 1, 0.06f));
                if (index == dropIndex) EditorGUI.DrawRect(new Rect(row.x, row.y - 1, row.width, 2), Color.white);
            }

            bool missing = Describe(item, out var label);
            using (new EditorGUI.DisabledScope(missing))
                GUI.Label(new Rect(row.x + 6, row.y, row.width - 6, row.height), label);

            if (!row.Contains(e.mousePosition)) return;
            switch (e.type)
            {
                case EventType.MouseMove:
                    Repaint();
                    break;
                case EventType.MouseDown when e.button == 0:
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
                    DragAndDrop.StartDrag(label.text);
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
            dropIndex = Mathf.Clamp(Mathf.RoundToInt(e.mousePosition.y / RowHeight), 0, Page.items.Count);

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
            else if (key == KeyCode.DownArrow && count > 0) cursor = Mathf.Min(cursor + 1, count - 1);
            else if (key == KeyCode.UpArrow && count > 0) cursor = Mathf.Max(cursor - 1, 0);
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

        /// <summary>Builds the row label; returns true when the favorite no longer exists.</summary>
        bool Describe(FavoritesStore.Item item, out GUIContent label)
        {
            label = new GUIContent(item.name);
            if (!GlobalObjectId.TryParse(item.id, out var id)) return true;
            var path = AssetDatabase.GUIDToAssetPath(id.assetGUID);
            if (string.IsNullOrEmpty(path)) return true;

            if (IsAsset(id))
            {
                if (!item.subAsset) label.text = Path.GetFileNameWithoutExtension(path);
                label.image = AssetDatabase.GetCachedIcon(path);
                label.tooltip = path;
                return false;
            }

            var obj = Resolve(item);
            if (obj)
            {
                label.text = item.name = obj.name;
                label.image = AssetPreview.GetMiniThumbnail(obj);
            }
            else label.image = EditorGUIUtility.IconContent("GameObject Icon").image;
            label.tooltip = $"{Path.GetFileNameWithoutExtension(path)} scene";
            return false;
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
                resolved.Clear();
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

        /// <summary>Shows the folder's contents in the Project window (two-column layout); falls back to pinging it.</summary>
        static void ShowFolder(Object folder)
        {
            var browserType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser");
            var browser = browserType?.GetField("s_LastInteractedProjectBrowser", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            var show = browserType?.GetMethod("ShowFolderContents", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(int), typeof(bool) }, null);
            if (browser != null && show != null) show.Invoke(browser, new object[] { folder.GetInstanceID(), true });
            else
            {
                Selection.activeObject = folder;
                EditorGUIUtility.PingObject(folder);
            }
        }
    }
}
