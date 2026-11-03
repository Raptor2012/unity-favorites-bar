using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FavoritesBar
{
    /// <summary>
    /// Per-user, per-project favorites: pages of assets, folders and scene objects, kept as
    /// <see cref="GlobalObjectId"/> strings in UserSettings so renames and moves keep working.
    /// </summary>
    [FilePath("UserSettings/FavoritesBar.asset", FilePathAttribute.Location.ProjectFolder)]
    public class FavoritesStore : ScriptableSingleton<FavoritesStore>
    {
        [Serializable]
        public class Item
        {
            public string id;
            public string name;
            public bool subAsset;
        }

        [Serializable]
        public class Page
        {
            public string name;
            public List<Item> items = new List<Item>();

            /// <summary>Inserts an item before <paramref name="index"/>; an item already on the page moves there instead.</summary>
            public void Insert(Item item, int index)
            {
                int old = items.FindIndex(x => x.id == item.id);
                if (old >= 0)
                {
                    items.RemoveAt(old);
                    if (old < index) index--;
                }
                items.Insert(Mathf.Clamp(index, 0, items.Count), item);
            }
        }

        public List<Page> pages = new List<Page>();
        public int current;

        public Page CurrentPage
        {
            get
            {
                if (pages.Count == 0) pages.Add(new Page { name = "Favorites" });
                current = Mathf.Clamp(current, 0, pages.Count - 1);
                return pages[current];
            }
        }

        public void Save() => Save(true);
    }

    /// <summary>Preferences > Favorites Bar.</summary>
    public static class FavoritesPrefs
    {
        public enum Shortcut { Alt, AltShift, CtrlAlt }

        const string Prefix = "FavoritesBar.";

        // Cached: Held() runs for every item the Project window draws, and EditorPrefs reads are native calls.
        static bool? enabled;
        static Shortcut? keys;

        public static bool Enabled
        {
            get => enabled ??= EditorPrefs.GetBool(Prefix + "Enabled", true);
            set
            {
                if (value == Enabled) return;
                enabled = value;
                EditorPrefs.SetBool(Prefix + "Enabled", value);
            }
        }

        public static Shortcut Keys
        {
            get => keys ??= (Shortcut)EditorPrefs.GetInt(Prefix + "Shortcut", 0);
            set
            {
                if (value == Keys) return;
                keys = value;
                EditorPrefs.SetInt(Prefix + "Shortcut", (int)value);
            }
        }

        /// <summary>True while the chosen shortcut is held.</summary>
        public static bool Held(Event e)
        {
            if (e == null) return false;
            var required = Keys switch
            {
                Shortcut.AltShift => EventModifiers.Alt | EventModifiers.Shift,
                Shortcut.CtrlAlt => EventModifiers.Alt | EventModifiers.Control,
                _ => EventModifiers.Alt
            };
            return (e.modifiers & required) == required;
        }

        [SettingsProvider]
        static SettingsProvider Provider() => new SettingsProvider("Preferences/Favorites Bar", SettingsScope.User)
        {
            keywords = new[] { "Favorites", "Bookmarks", "Alt", "Project" },
            guiHandler = _ =>
            {
                Enabled = EditorGUILayout.Toggle("Show while held over Project", Enabled);
                using (new EditorGUI.DisabledScope(!Enabled))
                    Keys = (Shortcut)EditorGUILayout.EnumPopup("Hold", Keys);
            }
        };
    }
}
