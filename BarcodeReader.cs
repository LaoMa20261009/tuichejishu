using System;
using System.Drawing;
using System.Linq;
using ZXing;
using ZXing.Common;

namespace CrabWeighing
{
    static class BoxBarcode
    {
        public static string[] Read(Bitmap bitmap)
        {
            var reader = new BarcodeReader { AutoRotate = true,
                Options = new DecodingOptions { TryHarder = true, PossibleFormats =
                    new[] { BarcodeFormat.CODE_128, BarcodeFormat.EAN_13, BarcodeFormat.QR_CODE } } };
            var found = reader.DecodeMultiple(bitmap);
            if (found == null || found.Length == 0)
            {
                Result single = reader.Decode(bitmap);
                if (single == null) return new string[0];
                found = new[] { single };
            }
            return found.Select(result => result.Text).Where(text =>
                !string.IsNullOrWhiteSpace(text) && text.Length <= 60 && !text.Any(char.IsControl)).Distinct().ToArray();
        }
    }
}
