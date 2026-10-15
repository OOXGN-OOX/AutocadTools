// SPDX-License-Identifier: LicenseRef-AutocadTools-Proprietary-NC-Attribution
// Copyright (c) 2026 OOXGN. See LICENSE in the repository root.
using System;
using System.IO;
using Newtonsoft.Json;
using System.Windows.Media.Imaging;
using AutocadTools.ViewModels;

namespace AutocadTools.Models
{
    public sealed class StampSignature
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; }
        public byte[] Png { get; set; }
        public byte[] Strokes { get; set; }
        [JsonIgnore] public BitmapSource Image
        {
            get
            {
                if (Png == null || Png.Length == 0) return null;
                using (var stream = new MemoryStream(Png))
                {
                    var image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    image.Freeze();
                    return image;
                }
            }
        }
    }

    public sealed class StampSignatureSlot : BaseViewModel
    {
        private StampSignature _signature;
        public string Key { get; }
        public string Label { get; }
        public StampSignatureSlot(string key, string label) { Key = key; Label = label; }
        public StampSignature Signature
        {
            get => _signature;
            set { if (SetProperty(ref _signature, value)) { OnPropertyChanged(nameof(Image)); OnPropertyChanged(nameof(Hint)); } }
        }
        public BitmapSource Image => Signature?.Image;
        public string Hint => Signature == null ? "Выбрать или создать подпись" : Signature.Name;
    }
}
