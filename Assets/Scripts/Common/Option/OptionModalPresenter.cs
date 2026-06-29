using System;
using R3;
using UnityEngine.UIElements;

namespace Common.Option
{
    public class OptionModalPresenter
    {
        private readonly OptionModel _optionModel;
        private readonly Action _onClose;
        private readonly Action _onBackToTitle;

        public OptionModalPresenter(OptionModel optionModel, Action onClose, Action onBackToTitle)
        {
            _optionModel = optionModel;
            _onClose = onClose;
            _onBackToTitle = onBackToTitle;
        }

        public void Setup(TemplateContainer modal, CompositeDisposable disposables)
        {
            Button closeButton = modal.Q<Button>("CloseButton");
            closeButton.clicked += _onClose;

            Button backToTitleButton = modal.Q<Button>("BackToTitleButton");
            backToTitleButton.clicked += _onBackToTitle;

            Slider bgmSlider = modal.Q<Slider>("BGMSlider");
            bgmSlider.value = _optionModel.BGMVolume.CurrentValue;
            _optionModel.BGMVolume
                .Subscribe(v => bgmSlider.SetValueWithoutNotify(v))
                .AddTo(disposables);
            bgmSlider.RegisterValueChangedCallback(evt => _optionModel.SetBGMVolume(evt.newValue));

            Slider seSlider = modal.Q<Slider>("SESlider");
            seSlider.value = _optionModel.SEVolume.CurrentValue;
            _optionModel.SEVolume
                .Subscribe(v => seSlider.SetValueWithoutNotify(v))
                .AddTo(disposables);
            seSlider.RegisterValueChangedCallback(evt => _optionModel.SetSEVolume(evt.newValue));
        }
    }
}
