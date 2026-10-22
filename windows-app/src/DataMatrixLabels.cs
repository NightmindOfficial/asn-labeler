using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using ZXing;
using ZXing.Common;
using ZXing.Datamatrix;
using ZXing.Datamatrix.Encoder;

namespace AsnLabeler
{
    static class EmbeddedBarcodeLibrary
    {
        public static void Initialize()
        {
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args)
            {
                if (new AssemblyName(args.Name).Name != "zxing") return null;
                using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("AsnLabeler.zxing.dll"))
                using (var buffer = new MemoryStream())
                {
                    if (source == null) throw new InvalidOperationException("Die eingebettete Barcode-Bibliothek fehlt.");
                    source.CopyTo(buffer);
                    return Assembly.Load(buffer.ToArray());
                }
            };
        }
    }

    static class LabelRenderer
    {
        internal const int QuietModules = 2;
        internal const int MinimumModuleDots = 3;

        internal static BitMatrix Encode(string text)
        {
            if (String.IsNullOrEmpty(text)) throw new ArgumentException(Loc.T("Barcode-Inhalt fehlt."));
            foreach (char c in text)
                if (c < 32 || c > 126) throw new ArgumentException(Loc.T("Der ASN-Barcode muss druckbares ASCII enthalten."));
            return new DataMatrixWriter().encode(text, BarcodeFormat.DATA_MATRIX, 0, 0,
                new Dictionary<EncodeHintType, object> {
                    { EncodeHintType.DATA_MATRIX_SHAPE, SymbolShapeHint.FORCE_SQUARE },
                    { EncodeHintType.MARGIN, 0 }
                });
        }

        public static Bitmap Render(string text, MediaInfo media, bool pfRightAligned = false, bool copy = false)
        {
            if (pfRightAligned && media.IsDieCut && media.WidthMm == 23 && media.LengthMm == 23)
            {
                using (var native = RenderCompatible(text, media, copy))
                {
                    var preview = new Bitmap(media.PixelWidth, media.PixelHeight, PixelFormat.Format24bppRgb);
                    preview.SetResolution(300, 300);
                    using (var g = Graphics.FromImage(preview))
                    {
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(native, new Rectangle(0, 0, preview.Width, preview.Height),
                            0, 0, native.Width, native.Height, GraphicsUnit.Pixel);
                    }
                    return preview;
                }
            }
            var area = media.RasterArea;
            bool circular = media.IsDieCut && media.WidthMm == media.LengthMm && media.WidthMm != 23;
            if (circular)
            {
                int inset = (int)Math.Ceiling(Math.Max(area.Width, area.Height) * 0.147);
                area.Inflate(-inset, -inset);
            }
            bool rotate = area.Height > area.Width * 1.5;
            using (var content = RenderContent(text, rotate ? area.Height : area.Width, rotate ? area.Width : area.Height, copy))
            {
                if (rotate) content.RotateFlip(RotateFlipType.Rotate90FlipNone);
                return Place(content, media, area, 1);
            }
        }

        internal static Bitmap RenderCompatible(string text, MediaInfo media, bool copy = false)
        {
            if (!media.IsDieCut || media.WidthMm != 23 || media.LengthMm != 23)
                throw new InvalidOperationException(Loc.T("Das Compatible-Profil benötigt 23×23-mm-Stanzlabels."));
            var area = media.RasterArea;
            using (var content = RenderContent(text, area.Height, area.Width, copy))
            {
                content.RotateFlip(RotateFlipType.Rotate90FlipNone);
                // Each 300-dpi row becomes exactly two 600-dpi rows. Physical
                // modules stay square, with no fractional edges or interpolation.
                return Place(content, media, area, 2);
            }
        }

        static Bitmap Place(Bitmap content, MediaInfo media, Rectangle area, int feedScale)
        {
            var result = new Bitmap(media.PixelWidth, media.PixelHeight * feedScale, PixelFormat.Format24bppRgb);
            try
            {
                result.SetResolution(300, 300 * feedScale);
                using (var g = Graphics.FromImage(result))
                {
                    g.Clear(Color.White);
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(content, new Rectangle(area.Left, area.Top * feedScale, area.Width, area.Height * feedScale),
                        0, 0, content.Width, content.Height, GraphicsUnit.Pixel);
                }
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        internal static Bitmap RenderContent(string text, int width, int height, bool copy = false)
        {
            var matrix = Encode(text);
            bool copyBesideAsn = copy && height < 120;
            int textSpace = copy && !copyBesideAsn ? 50 : 30;
            int available = Math.Min(width - 8, height - textSpace - 8);
            int module = Math.Min(10, available / (matrix.Width + QuietModules * 2));
            if (module < MinimumModuleDots)
                throw new InvalidOperationException(Loc.T("DataMatrix und Weißrand passen nicht scanbar auf diese Rolle. Bitte eine größere Rolle verwenden."));
            int box = (matrix.Width + QuietModules * 2) * module;
            int left = (width - box) / 2;
            int top = (height - textSpace - box) / 2;
            var result = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            try
            {
                result.SetResolution(300, 300);
                using (var g = Graphics.FromImage(result))
                {
                    g.Clear(Color.White);
                    g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                    for (int y = 0; y < matrix.Height; y++)
                        for (int x = 0; x < matrix.Width; x++)
                            if (matrix[x, y])
                                g.FillRectangle(Brushes.Black, left + (x + QuietModules) * module,
                                    top + (y + QuietModules) * module, module, module);
                    string human = text.StartsWith("ASN") ? "ASN " + text.Substring(3) : text;
                    if (copyBesideAsn) human += "  ! -COPY- !";
                    bool fits = false;
                    for (int size = 18; size >= 12; size--)
                    {
                        using (var font = MonoFont(size))
                        {
                            var measured = g.MeasureString(human, font);
                            if (measured.Width > width - 8 || measured.Height > 28) continue;
                            g.DrawString(human, font, Brushes.Black, (width - measured.Width) / 2, top + box + 2);
                            fits = true;
                            break;
                        }
                    }
                    if (!fits) throw new InvalidOperationException(Loc.T("Der ASN-Klartext passt nicht auf diese Rolle."));
                    if (copy && !copyBesideAsn)
                        using (var font = MonoFont(14))
                        {
                            const string marker = "! -COPY- !";
                            var measured = g.MeasureString(marker, font);
                            if (measured.Width > width - 8 || measured.Height > 20)
                                throw new InvalidOperationException(Loc.T("Der COPY-Vermerk passt nicht auf diese Rolle."));
                            g.DrawString(marker, font, Brushes.Black, (width - measured.Width) / 2, top + box + 27);
                        }
                }
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        static Font MonoFont(float size)
        {
            foreach (string name in new[] { "Cascadia Mono", "Consolas", "Lucida Console", "Courier New" })
            {
                var font = new Font(name, size, FontStyle.Bold, GraphicsUnit.Pixel);
                if (String.Equals(font.FontFamily.Name, name, StringComparison.OrdinalIgnoreCase)) return font;
                font.Dispose();
            }
            return new Font(FontFamily.GenericMonospace, size, FontStyle.Bold, GraphicsUnit.Pixel);
        }
    }

    static class DataMatrixTests
    {
        internal static void CheckDecode(Bitmap bitmap, string payload, List<string> failures)
        {
            var reader = new BarcodeReader { AutoRotate = true };
            reader.Options.PossibleFormats = new[] { BarcodeFormat.DATA_MATRIX };
            reader.Options.TryHarder = true;
            var result = reader.Decode(bitmap);
            if (result == null || result.Text != payload || result.BarcodeFormat != BarcodeFormat.DATA_MATRIX)
                failures.Add("DataMatrix round-trip failed: " + payload + " (" + bitmap.Width + "x" + bitmap.Height + ").");
        }

        public static IEnumerable<string> Run()
        {
            var failures = new List<string>();
            foreach (string payload in new[] { "TEST0001", "ASN00001", "ASN99999", "ASN100000", "ASN1234567", "ASN2147483647" })
            {
                foreach (var media in new[] { MediaInfo.DefaultDk11221(), MediaInfo.FromStatus(0x4A, 12, 0), MediaInfo.FromStatus(0x4A, 62, 0) })
                    using (var copy = LabelRenderer.Render(payload, media, false, true)) CheckDecode(copy, payload, failures);
                using (var copy = LabelRenderer.RenderCompatible(payload, MediaInfo.DefaultDk11221(), true)) CheckDecode(copy, payload, failures);
                foreach (var media in new[] { MediaInfo.DefaultDk11221(), MediaInfo.FromStatus(0x4A, 12, 0), MediaInfo.FromStatus(0x4A, 62, 0) })
                    using (var raster = LabelRenderer.Render(payload, media))
                        CheckDecode(raster, payload, failures);
                using (var preview = LabelRenderer.Render(payload, MediaInfo.DefaultDk11221(), true))
                    CheckDecode(preview, payload, failures);
                using (var native = LabelRenderer.RenderCompatible(payload, MediaInfo.DefaultDk11221()))
                {
                    if (native.Width != 272 || native.Height != 544 || native.VerticalResolution != 600)
                        failures.Add("Compatible raster dimensions or DPI changed.");
                    CheckDecode(native, payload, failures);
                    for (int y = 0; y < native.Height; y += 2)
                        for (int x = 0; x < native.Width; x++)
                            if (native.GetPixel(x, y).ToArgb() != native.GetPixel(x, y + 1).ToArgb())
                                throw new InvalidOperationException("Compatible raster has fractional 600-dpi cells.");
                }
                using (var content = LabelRenderer.RenderContent(payload, 236, 202))
                {
                    var matrix = LabelRenderer.Encode(payload);
                    int module = Math.Min(10, Math.Min(236 - 8, 202 - 30 - 8) / (matrix.Width + 4));
                    int box = (matrix.Width + 4) * module;
                    int left = (236 - box) / 2, top = (202 - 30 - box) / 2;
                    for (int y = 0; y < box; y++)
                        for (int x = 0; x < box; x++)
                        {
                            int mx = x / module - 2, my = y / module - 2;
                            bool expected = mx >= 0 && my >= 0 && mx < matrix.Width && my < matrix.Height && matrix[mx, my];
                            if ((content.GetPixel(left + x, top + y).R == 0) != expected)
                                throw new InvalidOperationException("DataMatrix module or quiet zone altered.");
                        }
                }
            }
            try { using (var small = LabelRenderer.RenderContent("ASN00001", 35, 35)) { } failures.Add("Too-small matrix accepted."); }
            catch (InvalidOperationException) { }
            return failures;
        }
    }
}
