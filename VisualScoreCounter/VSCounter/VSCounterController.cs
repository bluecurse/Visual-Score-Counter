using System;
using System.Linq;
using VisualScoreCounter.Core.Configuration;
using VisualScoreCounter.VSCounter.Configuration;
using VisualScoreCounter.Utils;
using UnityEngine;
using TMPro;
using CountersPlus.Counters.Interfaces;
using CountersPlus.Custom;
using CountersPlus.Utils;
using CountersPlus.Counters.NoteCountProcessors;
using HMUI;
using CountersPlus.ConfigModels;
using IPA.Utilities;
using UnityEngine.UI;
using BeatSaberMarkupLanguage;
using Zenject;
using Tweening;
using System.Reflection;

namespace VisualScoreCounter.VSCounter
{

    public class VSCounterController : ICounter, INoteEventHandler
    {

        private CounterSettings config;
        private readonly CanvasUtility canvasUtility;
        private readonly CustomConfigModel settings;
        [Inject] private CoreGameHUDController coreGameHUD;
        [Inject] private readonly RelativeScoreAndImmediateRankCounter relativeScoreAndImmediateRank;
        [Inject] ScoreController scoreController;
        [Inject] SongTimeTweeningManager uwuTweenyManager;
        [Inject] private GameplayCoreSceneSetupData setupData;
        [Inject] private NoteCountProcessor noteCountProcessor;

        // Ring vars
        private readonly string multiplierImageSpriteName = "Circle";
        private readonly Vector3 ringSize = Vector3.one * 1.175f;
        private float _currentPercentage = 0.0f;
        private bool bIsInReplay = false;

        private TMP_Text percentMajorText;
        private TMP_Text percentMinorText;

        private ImageView progressRing;
        private VSCounterTweenHelper vsCounterTweenHelper;

        private int notesLeft = 0;

        public VSCounterController(CanvasUtility canvasUtility, CustomConfigModel settings)
        {
            this.canvasUtility = canvasUtility;
            this.settings = settings;
            config = PluginConfig.Instance.CounterSettings;
        }

        static MethodBase ScoreSaber_playbackEnabled;

        public void CounterDestroy()
        {
            relativeScoreAndImmediateRank.relativeScoreOrImmediateRankDidChangeEvent -= OnRelativeScoreUpdate;
            scoreController.scoringForNoteFinishedEvent -= ScoreController_scoringForNoteFinishedEvent;
        }

        public void CounterInit()
        {

            ScoreSaber_playbackEnabled =
                IPA.Loader.PluginManager.GetPluginFromId("ScoreSaber")?
                .Assembly.GetType("ScoreSaber.Core.ReplaySystem.HarmonyPatches.PatchHandleHMDUnmounted")?
                .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);

            if (setupData.practiceSettings != null && setupData.practiceSettings.startInAdvanceAndClearNotes)
            {
                float startTime = setupData.practiceSettings.startSongTime;
                // This LINQ statement is to ensure compatibility with Practice Mode / Practice Plugin
                notesLeft = noteCountProcessor.Data.Count(x => x.time > startTime);
            } else {
                notesLeft = noteCountProcessor.NoteCount;
            }

            try
            {
                bIsInReplay = ScoreSaber_playbackEnabled != null && !(bool)ScoreSaber_playbackEnabled.Invoke(null, null);
            }
            catch { }

            if (bIsInReplay)
            {
                Plugin.Log.Debug("VisualScoreCounter : We are in a replay!");
            } else {
                Plugin.Log.Debug("VisualScoreCounter : We are NOT in a replay!");
            }

            if (HasNullReferences())
            {
                return;
            }

            InitVSCounter();

        }

        public bool HasNullReferences()
        {
            if (canvasUtility == null || settings == null)
            {

                Plugin.Log.Error("VisualScoreCounter : VSCounterController has a null reference and cannot initialize! Please file an issue on our github.");
                Plugin.Log.Error("The following objects are null:");

                if (canvasUtility == null)
                {
                    Plugin.Log.Error("- CanvasUtility");
                }

                if (settings == null)
                {
                    Plugin.Log.Error("- Settings");
                }

                return true;
            }

            return false;

        }

        private void InitVSCounter()
        {


            _currentPercentage = 100.0f;
            percentMajorText = canvasUtility.CreateTextFromSettings(settings);
            percentMajorText.fontSize = config.CounterFontSettings.WholeNumberFontSize;
            percentMinorText = canvasUtility.CreateTextFromSettings(settings);
            percentMinorText.fontSize = config.CounterFontSettings.FractionalNumberFontSize;
            if (config.CounterFontSettings.BloomFont)
            {
                percentMajorText.font = BloomFontAssetMaker.instance.BloomFontAsset();
                percentMinorText.font = BloomFontAssetMaker.instance.BloomFontAsset();
            }

            HUDCanvas currentSettings = canvasUtility.GetCanvasSettingsFromID(settings.CanvasID);

            var canvas = canvasUtility.GetCanvasFromID(settings.CanvasID);
            if (canvas != null)
            {
                Vector2 ringAnchoredPos = (canvasUtility.GetAnchoredPositionFromConfig(settings) * currentSettings.PositionScale);
                ringAnchoredPos = ringAnchoredPos + GetCounterOffset();

                ImageView backgroundImage = CreateRing(canvas);
                backgroundImage.rectTransform.anchoredPosition = ringAnchoredPos;
                backgroundImage.CrossFadeAlpha(0.05f, 1f, false);
                backgroundImage.transform.localScale = ComputeRingSize();
                backgroundImage.type = Image.Type.Simple;

                progressRing = CreateRing(canvas);
                progressRing.rectTransform.anchoredPosition = ringAnchoredPos;
                progressRing.transform.localScale = ComputeRingSize();
                if (config.BloomRing)
                {
                    progressRing.material = new Material(Shader.Find("UI/Default"));
                }
                vsCounterTweenHelper = progressRing.gameObject.AddComponent<VSCounterTweenHelper>();
            }

            if (config.HideBaseGameRankDisplay)
            {

                GameObject baseGameRank = FieldAccessor<CoreGameHUDController, GameObject>.GetAccessor("_immediateRankGO")(ref coreGameHUD);
                UnityEngine.Object.Destroy(baseGameRank.gameObject);

                GameObject baseGameScore = FieldAccessor<CoreGameHUDController, GameObject>.GetAccessor("_relativeScoreGO")(ref coreGameHUD);
                UnityEngine.Object.Destroy(baseGameScore.gameObject);

            }

            UpdateCounter();

            percentMajorText.rectTransform.anchoredPosition += new Vector2(config.CounterFontSettings.WholeNumberXOffset + config.CounterXOffset, config.CounterFontSettings.WholeNumberYOffset + config.CounterYOffset);
            percentMinorText.rectTransform.anchoredPosition += new Vector2(config.CounterFontSettings.FractionalNumberXOffset + config.CounterXOffset, config.CounterFontSettings.FractionalNumberYOffset + config.CounterYOffset);
            relativeScoreAndImmediateRank.relativeScoreOrImmediateRankDidChangeEvent += OnRelativeScoreUpdate;
            scoreController.scoringForNoteFinishedEvent += ScoreController_scoringForNoteFinishedEvent;

        }


        public void OnNoteCut(NoteData data, NoteCutInfo info)
        {
            if (ShouldProcessNote(data) && !noteCountProcessor.ShouldIgnoreNote(data)) DecrementCounter();
        }


        public void OnNoteMiss(NoteData data)
        {
            if (ShouldProcessNote(data) && !noteCountProcessor.ShouldIgnoreNote(data)) DecrementCounter();
        }


        private bool ShouldProcessNote(NoteData data)
            => data.gameplayType switch
            {
                NoteData.GameplayType.Normal => true,
                NoteData.GameplayType.BurstSliderHead => true,
                _ => false,
            };


        private void DecrementCounter()
        {
            --notesLeft;
            if (notesLeft == 0)
            {
                UpdateCounter();
            }
        }


        private ImageView CreateRing(Canvas canvas)
        {
            // Unfortunately, there is no guarantee that I have the CoreGameHUDController, since No Text and Huds
            // completely disables it from spawning. So, to be safe, we recreate this all from scratch.
            GameObject imageGameObject = new GameObject("Ring Image", typeof(RectTransform));
            imageGameObject.transform.SetParent(canvas.transform, false);
            ImageView newImage = imageGameObject.AddComponent<ImageView>();
            newImage.enabled = false;
            newImage.material = Utilities.ImageResources.NoGlowMat;
            newImage.sprite = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(x => x.name == multiplierImageSpriteName);
            newImage.type = Image.Type.Filled;
            newImage.fillClockwise = true;
            newImage.fillOrigin = 2;
            newImage.fillAmount = 1;
            newImage.fillMethod = Image.FillMethod.Radial360;
            newImage.enabled = true;
            return newImage;
        }

        private void UpdateCounter()
        {
            float percentage = GetCurrentPercentage();
            UpdateRing(percentage);
            UpdateScoreText(percentage);
        }

        private void UpdateRing(float percentage)
        {

            Color nextColor = GetColorForPercent(percentage);

            if (config.PercentageRingShowsNextColor)
            {
                nextColor = GetColorForPercent(percentage + 1);
            }

            if (progressRing)
            {
                progressRing.color = nextColor;
            }

            float ringFillAmount = ((float)percentage) % 1;

            progressRing.fillAmount = ringFillAmount;
            progressRing.SetVerticesDirty();

        }

        private void UpdateScoreText(float percentage)
        {
            int majorPercent = GetCurrentMajorPercent();
            int minorPercent = GetCurrentMinorPercent();

            Color percentMajorColor = GetColorForPercent(percentage);
            Color percentMinorColor = percentMajorColor;
            if (config.PercentageRingShowsNextColor)
            {
                percentMinorColor = GetColorForPercent(percentage + 1);
            }
            percentMajorText.text = string.Format("{0:D2}", majorPercent);
            percentMajorText.color = percentMajorColor;
            percentMinorText.text = string.Format("{0:D2}", minorPercent);
            percentMinorText.color = percentMinorColor;
        }

        private int GetCurrentMajorPercent()
        {
            int majorPart = (int)Math.Floor(GetCurrentPercentage());
            int minorPart = (int)Math.Round(GetCurrentPercentage() % 1, 2);
            return majorPart + minorPart;
        }

        private int GetCurrentMinorPercent()
        {
            int x = (int)((Math.Round(GetCurrentPercentage() % 1, 2)) * 100) % 100;
            return x;
        }

        private Vector3 ComputeRingSize()
        {
            return ((ringSize * config.RingScale) / 10.0f);
        }

        private Vector2 GetCounterOffset()
        {
            return new Vector2(config.CounterXOffset, config.CounterYOffset);
        }

        private Color GetColorForPercent(float Score)
        {
            Color outColor = Color.white;
            if (Score >= 100.0f)
            {
                outColor = config.PercentageColorSettings.Color_100;
            }

            // 99%
            if (Score >= 99.0f && Score < 100.0f)
            {
                outColor = config.PercentageColorSettings.Color_99;
            }

            // 98%
            if (Score >= 98.0f && Score < 99.0f)
            {
                outColor = config.PercentageColorSettings.Color_98;
            }

            // 97%
            if (Score >= 97.0f && Score < 98.0f)
            {
                outColor = config.PercentageColorSettings.Color_97;
            }

            // 96%
            if (Score >= 96.0f && Score < 97.0f)
            {
                outColor = config.PercentageColorSettings.Color_96;
            }

            // 95%
            if (Score >= 95.0f && Score < 96.0f)
            {
                outColor = config.PercentageColorSettings.Color_95;
            }

            // 94%
            if (Score >= 94.0f && Score < 95.0f)
            {
                outColor = config.PercentageColorSettings.Color_94;
            }

            // 93%
            if (Score >= 93.0f && Score < 94.0f)
            {
                outColor = config.PercentageColorSettings.Color_93;
            }

            // 92%
            if (Score >= 92.0f && Score < 93.0f)
            {
                outColor = config.PercentageColorSettings.Color_92;
            }

            // 91%
            if (Score >= 91.0f && Score < 92.0f)
            {
                outColor = config.PercentageColorSettings.Color_91;
            }

            // 90%
            if (Score >= 90.0f && Score < 91.0f)
            {
                outColor = config.PercentageColorSettings.Color_90;
            }

            // 89%
            if (Score >= 89.0f && Score < 90.0f)
            {
                outColor = config.PercentageColorSettings.Color_89;
            }

            // 88%
            if (Score >= 88.0f && Score < 89.0f)
            {
                outColor = config.PercentageColorSettings.Color_88;
            }

            // 80%
            if (Score >= 80.0f && Score < 88.0f)
            {
                outColor = config.PercentageColorSettings.Color_80;
            }

            // 65%
            if (Score >= 65.0f && Score < 80.0f)
            {
                outColor = config.PercentageColorSettings.Color_65;
            }

            // 50%
            if (Score >= 50.0f && Score < 65.0f)
            {
                outColor = config.PercentageColorSettings.Color_50;
            }

            // 35%
            if (Score >= 35.0f && Score < 50.0f)
            {
                outColor = config.PercentageColorSettings.Color_35;
            }

            // 20%
            if (Score >= 20.0f && Score < 35.0f)
            {
                outColor = config.PercentageColorSettings.Color_20;
            }

            // 0%
            if (Score >= 0.0f && Score < 20.0f)
            {
                outColor = config.PercentageColorSettings.Color_0;
            }

            return outColor;

        }


        private float GetCurrentPercentage()
        {

            float relativeScore = relativeScoreAndImmediateRank.relativeScore * 100;
            if (relativeScore <= 0)
            {
                relativeScore = 100.0f;
            }
            return (float) Math.Round(relativeScore, 2);
        }

        private void OnRelativeScoreUpdate()
        {
            // Only use this event if we're not in a replay, since updates in game in real time are actually accurate
            if (!bIsInReplay || notesLeft < 8)
            {
                UpdateCounter();
            }
        }

        private void ScoreController_scoringForNoteFinishedEvent(ScoringElement scoringElement)
        {
            // Only use this event if we're in a replay, since replay scoring is very scuffed
            if (bIsInReplay && notesLeft > 8) {
                UpdateCounter();
            }
        }

    }

}
