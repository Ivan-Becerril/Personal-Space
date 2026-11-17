using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PersonalSpace
{
    public sealed class PersonalSpaceMainMenu : MonoBehaviour
    {
        [SerializeField] private Button play = null;
        [SerializeField] private Button quit = null;
        [SerializeField] private InputSystemUIInputModule input = null;

        private bool loading;

        private void Awake()
        {
            if (input != null && input.actionsAsset == null)
                input.AssignDefaultActions();
            play.onClick.AddListener(PlayCity);
            quit.onClick.AddListener(Quit);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && !loading && keyboard.escapeKey.wasPressedThisFrame)
                Quit();
        }

        private void PlayCity()
        {
            if (loading)
                return;

            loading = SceneManager.LoadSceneAsync("CityStage") != null;
            if (!loading)
                return;

            play.interactable = false;
            quit.interactable = false;
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
