using UnityEditor;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    public static class MusiderunMenu
    {
        private const string MenuRoot = "Tools/works.mmzk.util.musiderun/";

        [MenuItem(MenuRoot + "Open Window")]
        public static void OpenWindow()
        {
            MusiderunWindow.ShowWindow();
        }

        [MenuItem(MenuRoot + "Create Settings JSON")]
        public static void CreateSettingsJson()
        {
            MusiderunSettingsJsonStore.EnsureJsonExists();
            EditorUtility.RevealInFinder(MusiderunSettingsJsonStore.ResolveAbsolutePath());
        }
    }
}
