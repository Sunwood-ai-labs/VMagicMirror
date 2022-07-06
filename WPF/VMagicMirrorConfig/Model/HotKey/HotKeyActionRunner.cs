﻿namespace Baku.VMagicMirrorConfig
{
    internal class HotKeyActionRunner
    {
        public HotKeyActionRunner() : this(
            ModelResolver.Instance.Resolve<HotKeySettingModel>(),
            ModelResolver.Instance.Resolve<HotKeyModel>(),
            ModelResolver.Instance.Resolve<LayoutSettingModel>(),
            ModelResolver.Instance.Resolve<WordToMotionSettingModel>()
            )
        {
        }

        public HotKeyActionRunner(
            HotKeySettingModel setting,
            HotKeyModel model,
            LayoutSettingModel layoutSetting,
            WordToMotionSettingModel wordToMotionSetting
            )
        {
            _setting = setting;
            _model = model;
            _layoutSetting = layoutSetting;
            _wordToMotionSetting = wordToMotionSetting;

            _model.ActionRequested += OnActionRequested;
        }

        private HotKeyModel _model;
        private HotKeySettingModel _setting;
        private LayoutSettingModel _layoutSetting;
        private WordToMotionSettingModel _wordToMotionSetting;

        private void OnActionRequested(HotKeyActionContent content)
        {
            //if (!_setting.EnableHotKey.Value)
            //{
            //    return;
            //}

            LogOutput.Instance.Write($"run hotkey action, content={content.Action}, arg={content.ArgNumber}");
            switch (content.Action)
            {
                case HotKeyActions.SetCamera:
                    _layoutSetting.QuickLoadViewPoint(content.ArgNumber);
                    break;
                case HotKeyActions.CallWtm:
                    _wordToMotionSetting.Play(content.ArgNumber);
                    break;
                case HotKeyActions.None:
                default:
                    //何もしない
                    break;
            }
        }
    }
}
