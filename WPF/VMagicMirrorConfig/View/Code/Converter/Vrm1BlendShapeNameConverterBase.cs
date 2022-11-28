﻿using System;
using System.Globalization;
using System.Windows.Data;

namespace Baku.VMagicMirrorConfig.View
{
    public abstract class Vrm1BlendShapeNameConverterBase : IMultiValueConverter
    {
        public abstract string FallbackStringKey { get; }

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length != 2 || !(values[0] is string mainValue) || !(values[1] is string))
            {
                return Binding.DoNothing;
            }

            //NOTE: values[1]には言語名が入ってる想定だが、Localized.GetStringで間接的に使うので直接参照しないでよい
            return string.IsNullOrEmpty(mainValue)
                ? LocalizedString.GetString(FallbackStringKey) :
                DefaultBlendShapeNameStore.GetVrm10KeyName(mainValue);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => new object[] { Binding.DoNothing };
    }
}
