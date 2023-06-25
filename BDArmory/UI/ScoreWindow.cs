using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using BDArmory.Competition;
using BDArmory.Settings;
using BDArmory.Utils;

namespace BDArmory.UI
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class ScoreWindow : MonoBehaviour
    {
        #region Fields
        public static ScoreWindow Instance;
        public bool _ready = false;

        static int _guiCheckIndexScores = -1;
        Vector2 windowSize = new Vector2(200, 100);
        bool resizingWindow = false;
        bool autoResizingWindow = true;
        Vector2 scoreScrollPos = default;
        Dictionary<string, NumericInputField> scoreWeights;
        #endregion

        #region Styles
        bool stylesConfigured = false;
        GUIStyle leftLabel;
        GUIStyle rightLabel;
        GUIStyle inputFieldStyle;
        #endregion

        private void Awake()
        {
            if (Instance)
                Destroy(this);
            Instance = this;
        }

        private void Start()
        {
            _ready = false;
            StartCoroutine(WaitForBdaSettings());

            // Score weight fields
            scoreWeights = TournamentScores.weights.ToDictionary(kvp => kvp.Key, kvp => gameObject.AddComponent<NumericInputField>().Initialise(0, kvp.Value));
        }

        private IEnumerator WaitForBdaSettings()
        {
            yield return new WaitUntil(() => BDArmorySetup.Instance is not null);

            if (_guiCheckIndexScores < 0) _guiCheckIndexScores = GUIUtils.RegisterGUIRect(new Rect());
            if (_guiCheckIndexWeights < 0) _guiCheckIndexWeights = GUIUtils.RegisterGUIRect(new Rect());
            _ready = true;
            AdjustWindowRect(new Vector2(BDArmorySetup.WindowRectScores.width, BDArmorySetup.WindowRectScores.height));
        }

        void ConfigureStyles()
        {
            stylesConfigured = true;
            inputFieldStyle = new GUIStyle(GUI.skin.textField);
            inputFieldStyle.alignment = TextAnchor.MiddleRight;
            leftLabel = new GUIStyle();
            leftLabel.alignment = TextAnchor.MiddleLeft;
            leftLabel.normal.textColor = Color.white;
            leftLabel.wordWrap = true;
            rightLabel = new GUIStyle(leftLabel);
            rightLabel.alignment = TextAnchor.MiddleRight;
            rightLabel.wordWrap = false;
        }

        private void OnGUI()
        {
            if (!(_ready && BDArmorySettings.SHOW_SCORE_WINDOW && (BDArmorySetup.GAME_UI_ENABLED || BDArmorySettings.SCORES_PERSIST_UI) && HighLogic.LoadedSceneIsFlight))
                return;

            if (!stylesConfigured) ConfigureStyles();

            if (resizingWindow && Event.current.type == EventType.MouseUp) { resizingWindow = false; }
            AdjustWindowRect(windowSize);
            BDArmorySetup.SetGUIOpacity();
            BDArmorySetup.WindowRectScores = GUILayout.Window(
                GUIUtility.GetControlID(FocusType.Passive),
                BDArmorySetup.WindowRectScores,
                WindowScores,
                StringUtils.Localize("#LOC_BDArmory_BDAScores_Title"),//"BDA Scores"
                BDArmorySetup.BDGuiSkin.window
            );
            if (weightsVisible)
            {
                weightsWindowRect = GUILayout.Window(
                    GUIUtility.GetControlID(FocusType.Passive),
                    weightsWindowRect,
                    WindowWeights,
                    StringUtils.Localize("#LOC_BDArmory_BDAScores_Weights"), // "Score Weights"
                    BDArmorySetup.BDGuiSkin.window
                );
            }
            BDArmorySetup.SetGUIOpacity(false);
            GUIUtils.UpdateGUIRect(BDArmorySetup.WindowRectScores, _guiCheckIndexScores);
        }

        #region Scores
        private void AdjustWindowRect(Vector2 size)
        {
            if (!autoResizingWindow)
            {
                size.x = Mathf.Max(size.x, 150);
                size.y = Mathf.Max(size.y, 70); // The ScrollView won't let us go smaller than this.
                BDArmorySetup.WindowRectScores.width = size.x;
                BDArmorySetup.WindowRectScores.height = size.y;
            }
            GUIUtils.RepositionWindow(ref BDArmorySetup.WindowRectScores);
            windowSize = BDArmorySetup.WindowRectScores.size;
        }

        private void WindowScores(int id)
        {
            if (GUI.Button(new Rect(0, 0, 24, 24), "UI", BDArmorySettings.SCORES_PERSIST_UI ? BDArmorySetup.BDGuiSkin.box : BDArmorySetup.BDGuiSkin.button)) { BDArmorySettings.SCORES_PERSIST_UI = !BDArmorySettings.SCORES_PERSIST_UI; }
            GUI.DragWindow(new Rect(24, 0, windowSize.x - 24 * 3, 24));
            if (GUI.Button(new Rect(windowSize.x - 48, 0, 24, 24), "W", weightsVisible ? BDArmorySetup.SelectedButtonStyle : BDArmorySetup.ButtonStyle)) SetWeightsVisible(!weightsVisible);
            if (GUI.Button(new Rect(windowSize.x - 24, 0, 24, 24), " X", BDArmorySetup.CloseButtonStyle)) SetVisible(false);

            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandHeight(autoResizingWindow));
            if (!autoResizingWindow) scoreScrollPos = GUILayout.BeginScrollView(scoreScrollPos);
            using (var scoreField = BDATournament.Instance.GetRankedScores.GetEnumerator())
                while (scoreField.MoveNext())
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(scoreField.Current.Key, leftLabel);
                    GUILayout.Label($"{scoreField.Current.Value,7:F3}", rightLabel);
                    GUILayout.EndHorizontal();
                }
            if (!autoResizingWindow) GUILayout.EndScrollView();
            GUILayout.EndVertical();

            #region Resizing
            var resizeRect = new Rect(windowSize.x - 16, windowSize.y - 16, 16, 16);
            GUI.DrawTexture(resizeRect, GUIUtils.resizeTexture, ScaleMode.StretchToFill, true);
            if (Event.current.type == EventType.MouseDown && resizeRect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 1) // Right click - reset to auto-resizing the height.
                {
                    autoResizingWindow = true;
                    resizingWindow = false;
                    ResetWindowSize();
                }
                else
                {
                    autoResizingWindow = false;
                    resizingWindow = true;
                }
            }
            if (resizingWindow && Event.current.type == EventType.Repaint)
            { windowSize += Mouse.delta; }
            #endregion
            GUIUtils.UseMouseEventInRect(BDArmorySetup.WindowRectScores);
        }

        public void SetVisible(bool visible)
        {
            BDArmorySettings.SHOW_SCORE_WINDOW = visible;
            GUIUtils.SetGUIRectVisible(_guiCheckIndexScores, visible);
        }
        public bool IsVisible => BDArmorySettings.SHOW_SCORE_WINDOW;

        /// <summary>
        /// Reset the window size so that the height is tight.
        /// </summary>
        public void ResetWindowSize()
        {
            if (autoResizingWindow)
            {
                BDArmorySetup.WindowRectScores.height = 0;
            }
        }
        #endregion

        #region Weights
        internal static int _guiCheckIndexWeights = -1;
        bool weightsVisible = false;
        Rect weightsWindowRect = new Rect(0, 0, 300, 500);
        Vector2 weightsScrollPos = default;

        void SetWeightsVisible(bool visible)
        {
            weightsVisible = visible;
            GUIUtils.SetGUIRectVisible(_guiCheckIndexWeights, visible);
            if (visible)
            {
                weightsWindowRect.y = BDArmorySetup.WindowRectScores.y;
                weightsWindowRect.x = BDArmorySetup.WindowRectScores.x + windowSize.x;
            }
            else
            {
                foreach (var weight in scoreWeights)
                {
                    weight.Value.tryParseValueNow();
                    TournamentScores.weights[weight.Key] = (float)weight.Value.currentValue;
                }
                BDATournament.Instance.RecomputeScores();
            }
        }
        void WindowWeights(int id)
        {
            GUI.DragWindow(new Rect(0, 0, weightsWindowRect.width - 24, 24));
            if (GUI.Button(new Rect(weightsWindowRect.width - 24, 0, 24, 24), " X", BDArmorySetup.CloseButtonStyle)) SetWeightsVisible(false);
            GUILayout.BeginVertical(GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
            weightsScrollPos = GUILayout.BeginScrollView(weightsScrollPos, GUI.skin.box);
            var now = Time.time;
            foreach (var weight in scoreWeights)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(weight.Key);
                weight.Value.tryParseValue(GUILayout.TextField(weight.Value.possibleValue, 10, inputFieldStyle, GUILayout.Width(80)));
                if (TournamentScores.weights[weight.Key] != (float)weight.Value.currentValue)
                {
                    TournamentScores.weights[weight.Key] = (float)weight.Value.currentValue;
                    BDATournament.Instance.RecomputeScores();
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUIUtils.RepositionWindow(ref weightsWindowRect);
            GUIUtils.UpdateGUIRect(weightsWindowRect, _guiCheckIndexWeights);
            GUIUtils.UseMouseEventInRect(weightsWindowRect);
        }
        #endregion
    }
}
