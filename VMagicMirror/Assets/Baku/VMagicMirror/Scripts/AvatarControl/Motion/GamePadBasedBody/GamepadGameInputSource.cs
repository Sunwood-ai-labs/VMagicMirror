using System;
using UniRx;
using UnityEngine;

namespace Baku.VMagicMirror.GameInput
{
    public enum GamepadMoveStickType
    {
        None,
        LeftStick,
        RightStick,
        LeftArrowButton,
    }

    //NOTE: めんどくさいからトリガーを一旦省いてるが、別に入れてもいい
    public enum GamepadLocomotionButtonAssign
    {
        None,
        A,
        B,
        X,
        Y,
    }

    public class GamepadGameInputSource : IGameInputSource, IDisposable
    {
        bool IGameInputSource.IsActive => _isActive;
        Vector2 IGameInputSource.MoveInput => _moveInput;
        bool IGameInputSource.IsCrouching => _isCrouching;
        public IObservable<Unit> Jump => _jump;

        private readonly XInputGamePad _gamepad;

        private bool _isActive;
        private Vector2 _moveInput;
        private bool _isCrouching;
        private readonly Subject<Unit> _jump = new Subject<Unit>();

        private readonly ReactiveProperty<GamepadMoveStickType> _stickType =
            new ReactiveProperty<GamepadMoveStickType>(GamepadMoveStickType.None);

        public GamepadKey JumpButton { get; set; } = GamepadKey.Unknown;
        public GamepadKey CrouchButton { get; set; } = GamepadKey.Unknown;

        private CompositeDisposable _disposable;

        //TODO: 設定も受け取らないといけない
        public GamepadGameInputSource(XInputGamePad gamePad)
        {
            _gamepad = gamePad;
        }

        /// <summary>
        /// trueで呼び出すとゲームパッドの入力監視を開始する。
        /// falseで呼び出すと入力監視を終了する。必要ないうちは切っておくのを想定している
        /// </summary>
        /// <param name="active"></param>
        public void SetActive(bool active)
        {
            if (_isActive == active)
            {
                return;
            }

            _isActive = active;
            _disposable?.Dispose();
            if (!active)
            {
                return;
            }

            _disposable = new CompositeDisposable();
            _gamepad.ButtonUpDown
                .Subscribe(OnButtonUpDown)
                .AddTo(_disposable);

            _stickType.Select(s => s switch
                {
                    GamepadMoveStickType.LeftStick => _gamepad.LeftStickPosition,
                    GamepadMoveStickType.RightStick => _gamepad.RightStickPosition,
                    GamepadMoveStickType.LeftArrowButton => _gamepad
                        .ObserveEveryValueChanged(g => g.ArrowButtonsStickPosition),
                })
                .Switch()
                .Subscribe(OnMoveInputChanged)
                .AddTo(_disposable);
        }
        
        private void OnButtonUpDown(GamepadKeyData data)
        {
            if (data.Key == JumpButton && data.IsPressed)
            {
                _jump.OnNext(Unit.Default);
            }

            if (data.Key == CrouchButton)
            {
                _isCrouching = data.IsPressed;
            }
        }

        private void OnMoveInputChanged(Vector2Int moveInput)
        {
            var input = new Vector2(moveInput.x * 1f / 32768f, moveInput.y * 1f / 32768f);

            //NOTE: 更に「カメラに向かって奥方向かどうか」みたいなフラグも配慮したい気がする
            _moveInput = input;
        }

        void IDisposable.Dispose() => _disposable?.Dispose();
    }
}
