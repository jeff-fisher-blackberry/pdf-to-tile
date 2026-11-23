using System;
using OpenCvSharp;

namespace pdf_to_Tiles.Services
{
    /// <summary>
    /// Modes for color-matched chit edge cleanup.
    /// </summary>
    public enum EdgeCleanMode
    {
        TargetedReplace,
        SmartInpaint,
        SolidFlood
    }

    /// <summary>
    /// High-performance wargame chit image editing algorithms ported and adapted from GTL Text Editor.
    /// Operates on 4-channel BGRA (CV_8UC4) matrices with minimum dimension guards.
    /// </summary>
    public static class ChitEditorService
    {
        private const int MinDimension = 8;

        /// <summary>
        /// Applies a directional 3D raised-edge gradient bevel (highlight from top-left, shadow on bottom-right)
        /// around the perimeter of the chit.
        /// </summary>
        public static Mat ApplyBevel(Mat src, int borderSize, Vec4b? highlightColor = null, Vec4b? shadowColor = null)
        {
            if (src == null || src.IsDisposed || src.Width < MinDimension || src.Height < MinDimension)
            {
                throw new ArgumentException("Invalid source image matrix for bevel operation.", nameof(src));
            }

            int b = Math.Clamp(borderSize, 1, Math.Min(src.Width / 3, src.Height / 3));
            if (b <= 0) return src.Clone();

            Mat dst = src.Clone();
            int width = dst.Width;
            int height = dst.Height;

            // Default highlight: warm cream / soft beige; default shadow: deep charcoal / black
            Vec4b hi = highlightColor ?? new Vec4b(235, 245, 252, 255); // B, G, R, A
            Vec4b sh = shadowColor ?? new Vec4b(18, 18, 20, 255);

            var indexer = dst.GetGenericIndexer<Vec4b>();

            // 1. Top Edge Highlight
            for (int y = 0; y < b; y++)
            {
                float t = 1.0f - ((float)y / b);
                float strength = t * 0.70f;
                int startX = y;
                int endX = width - 1 - y;

                for (int x = startX; x <= endX; x++)
                {
                    Vec4b p = indexer[y, x];
                    if (p.Item3 == 0) continue; // Skip fully transparent pixels

                    byte nb = (byte)Math.Clamp(Math.Round(p.Item0 * (1f - strength) + hi.Item0 * strength), 0, 255);
                    byte ng = (byte)Math.Clamp(Math.Round(p.Item1 * (1f - strength) + hi.Item1 * strength), 0, 255);
                    byte nr = (byte)Math.Clamp(Math.Round(p.Item2 * (1f - strength) + hi.Item2 * strength), 0, 255);
                    indexer[y, x] = new Vec4b(nb, ng, nr, p.Item3);
                }
            }

            // 2. Left Edge Highlight
            for (int x = 0; x < b; x++)
            {
                float t = 1.0f - ((float)x / b);
                float strength = t * 0.70f;
                int startY = x;
                int endY = height - 1 - x;

                for (int y = startY; y <= endY; y++)
                {
                    Vec4b p = indexer[y, x];
                    if (p.Item3 == 0) continue;

                    byte nb = (byte)Math.Clamp(Math.Round(p.Item0 * (1f - strength) + hi.Item0 * strength), 0, 255);
                    byte ng = (byte)Math.Clamp(Math.Round(p.Item1 * (1f - strength) + hi.Item1 * strength), 0, 255);
                    byte nr = (byte)Math.Clamp(Math.Round(p.Item2 * (1f - strength) + hi.Item2 * strength), 0, 255);
                    indexer[y, x] = new Vec4b(nb, ng, nr, p.Item3);
                }
            }

            // 3. Bottom Edge Shadow
            for (int d = 0; d < b; d++)
            {
                int y = height - 1 - d;
                float t = 1.0f - ((float)d / b);
                float strength = t * 0.70f;
                int startX = d;
                int endX = width - 1 - d;

                for (int x = startX; x <= endX; x++)
                {
                    Vec4b p = indexer[y, x];
                    if (p.Item3 == 0) continue;

                    byte nb = (byte)Math.Clamp(Math.Round(p.Item0 * (1f - strength) + sh.Item0 * strength), 0, 255);
                    byte ng = (byte)Math.Clamp(Math.Round(p.Item1 * (1f - strength) + sh.Item1 * strength), 0, 255);
                    byte nr = (byte)Math.Clamp(Math.Round(p.Item2 * (1f - strength) + sh.Item2 * strength), 0, 255);
                    indexer[y, x] = new Vec4b(nb, ng, nr, p.Item3);
                }
            }

            // 4. Right Edge Shadow
            for (int d = 0; d < b; d++)
            {
                int x = width - 1 - d;
                float t = 1.0f - ((float)d / b);
                float strength = t * 0.70f;
                int startY = d;
                int endY = height - 1 - d;

                for (int y = startY; y <= endY; y++)
                {
                    Vec4b p = indexer[y, x];
                    if (p.Item3 == 0) continue;

                    byte nb = (byte)Math.Clamp(Math.Round(p.Item0 * (1f - strength) + sh.Item0 * strength), 0, 255);
                    byte ng = (byte)Math.Clamp(Math.Round(p.Item1 * (1f - strength) + sh.Item1 * strength), 0, 255);
                    byte nr = (byte)Math.Clamp(Math.Round(p.Item2 * (1f - strength) + sh.Item2 * strength), 0, 255);
                    indexer[y, x] = new Vec4b(nb, ng, nr, p.Item3);
                }
            }

            return dst;
        }

        /// <summary>
        /// Clips/rounds the 4 corner pixels of the chit using transparency or a specified key color.
        /// </summary>
        public static Mat ApplyCornerClip(Mat src, int cornerSize, Vec4b? clipColor = null)
        {
            if (src == null || src.IsDisposed || src.Width < MinDimension || src.Height < MinDimension)
            {
                throw new ArgumentException("Invalid source image matrix for corner clip.", nameof(src));
            }

            int c = Math.Clamp(cornerSize, 1, Math.Min(src.Width / 2, src.Height / 2));
            Mat dst = src.Clone();
            int width = dst.Width;
            int height = dst.Height;
            Vec4b clr = clipColor ?? new Vec4b(0, 0, 0, 0);

            var indexer = dst.GetGenericIndexer<Vec4b>();

            for (int y = 0; y < c; y++)
            {
                for (int x = 0; x < c; x++)
                {
                    // Upper-Left corner
                    if (x + y < c)
                    {
                        indexer[y, x] = clr;
                    }

                    // Upper-Right corner
                    int dxR = width - 1 - x;
                    if (x + y < c)
                    {
                        indexer[y, dxR] = clr;
                    }

                    // Lower-Left corner
                    int dyB = height - 1 - y;
                    if (x + y < c)
                    {
                        indexer[dyB, x] = clr;
                    }

                    // Lower-Right corner
                    if (x + y < c)
                    {
                        indexer[dyB, dxR] = clr;
                    }
                }
            }

            return dst;
        }

        /// <summary>
        /// Uniformly trims N pixels from all 4 borders inward, reducing chit dimensions symmetrically.
        /// </summary>
        public static Mat ApplyBorderTrim(Mat src, int trimPx)
        {
            if (src == null || src.IsDisposed)
            {
                throw new ArgumentException("Invalid source matrix for border trim.", nameof(src));
            }

            int maxTrim = Math.Min((src.Width - MinDimension) / 2, (src.Height - MinDimension) / 2);
            int t = Math.Clamp(trimPx, 1, Math.Max(1, maxTrim));

            int newW = src.Width - (2 * t);
            int newH = src.Height - (2 * t);

            if (newW < MinDimension || newH < MinDimension)
            {
                return src.Clone();
            }

            Rect roi = new Rect(t, t, newW, newH);
            using var sub = new Mat(src, roi);
            return sub.Clone();
        }

        /// <summary>
        /// Clears an outer margin of bufferPx on all 4 sides to transparent (or specified key color).
        /// Replicates the Clear button from GTL Text Editor to eliminate scanner margin bleed.
        /// </summary>
        public static Mat ApplyClearBorder(Mat src, int bufferPx, Vec4b? clearColor = null)
        {
            if (src == null || src.IsDisposed || src.Width < MinDimension || src.Height < MinDimension)
            {
                throw new ArgumentException("Invalid source matrix for clear border.", nameof(src));
            }

            int b = Math.Clamp(bufferPx, 1, Math.Min(src.Width / 3, src.Height / 3));
            Mat dst = src.Clone();
            int width = dst.Width;
            int height = dst.Height;
            Vec4b clr = clearColor ?? new Vec4b(0, 0, 0, 0);

            var indexer = dst.GetGenericIndexer<Vec4b>();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (x < b || x >= width - b || y < b || y >= height - b)
                    {
                        indexer[y, x] = clr;
                    }
                }
            }

            return dst;
        }

        /// <summary>
        /// Stretches/resamples the chit to the exact target width and height using Lanczos4 interpolation.
        /// </summary>
        public static Mat ApplyStretch(Mat src, int targetW, int targetH)
        {
            if (src == null || src.IsDisposed)
            {
                throw new ArgumentException("Invalid source matrix for stretch.", nameof(src));
            }

            int w = Math.Max(MinDimension, targetW);
            int h = Math.Max(MinDimension, targetH);

            Mat dst = new Mat();
            Cv2.Resize(src, dst, new OpenCvSharp.Size(w, h), 0, 0, InterpolationFlags.Lanczos4);
            return dst;
        }

        /// <summary>
        /// Resizes canvas around center: crops inwards if target is smaller, expands canvas if target is larger.
        /// Newly exposed outer margins are filled with the transparent color or specified key.
        /// </summary>
        public static Mat ApplyResize(Mat src, int targetW, int targetH, Vec4b? fillColor = null)
        {
            if (src == null || src.IsDisposed)
            {
                throw new ArgumentException("Invalid source matrix for resize.", nameof(src));
            }

            int w = Math.Max(MinDimension, targetW);
            int h = Math.Max(MinDimension, targetH);

            if (w == src.Width && h == src.Height)
            {
                return src.Clone();
            }

            Vec4b fill = fillColor ?? new Vec4b(0, 0, 0, 0);
            Scalar fillScalar = new Scalar(fill.Item0, fill.Item1, fill.Item2, fill.Item3);

            Mat dst = new Mat(new OpenCvSharp.Size(w, h), MatType.CV_8UC4, fillScalar);

            int copyW = Math.Min(src.Width, w);
            int copyH = Math.Min(src.Height, h);

            int srcX = Math.Max(0, (src.Width - w) / 2);
            int srcY = Math.Max(0, (src.Height - h) / 2);

            int dstX = Math.Max(0, (w - src.Width) / 2);
            int dstY = Math.Max(0, (h - src.Height) / 2);

            Rect srcRoi = new Rect(srcX, srcY, copyW, copyH);
            Rect dstRoi = new Rect(dstX, dstY, copyW, copyH);

            using var srcCrop = new Mat(src, srcRoi);
            using var dstSub = new Mat(dst, dstRoi);
            srcCrop.CopyTo(dstSub);

            return dst;
        }

        /// <summary>
        /// Samples an inner perimeter band (depth+1 to depth+4 px) to statistically determine the modal
        /// (dominant) counter background color, ignoring text, numbers, and transparency.
        /// </summary>
        public static Vec4b SampleDominantBackgroundColor(Mat src, int depth = 2)
        {
            if (src == null || src.IsDisposed || src.Width < MinDimension || src.Height < MinDimension)
            {
                return new Vec4b(200, 200, 200, 255);
            }

            int width = src.Width;
            int height = src.Height;

            int innerStart = Math.Clamp(depth + 1, 1, Math.Min(width / 3, height / 3));
            int innerEnd = Math.Clamp(depth + 4, innerStart, Math.Min(width / 2, height / 2));

            var indexer = src.GetGenericIndexer<Vec4b>();

            // 3D quantized histogram: 32 bins per channel (5 bits: >> 3) -> 32,768 bins
            int[] histogram = new int[32768];
            long[] sumB = new long[32768];
            long[] sumG = new long[32768];
            long[] sumR = new long[32768];

            void ProcessPixel(int x, int y)
            {
                Vec4b p = indexer[y, x];
                if (p.Item3 == 0) return; // Skip transparent pixels

                int bBin = p.Item0 >> 3;
                int gBin = p.Item1 >> 3;
                int rBin = p.Item2 >> 3;
                int binIdx = (bBin << 10) | (gBin << 5) | rBin;

                histogram[binIdx]++;
                sumB[binIdx] += p.Item0;
                sumG[binIdx] += p.Item1;
                sumR[binIdx] += p.Item2;
            }

            for (int y = innerStart; y <= innerEnd; y++)
            {
                for (int x = innerStart; x < width - innerStart; x++)
                {
                    ProcessPixel(x, y);                   // Top inner band
                    ProcessPixel(x, height - 1 - y);       // Bottom inner band
                }
            }

            for (int x = innerStart; x <= innerEnd; x++)
            {
                for (int y = innerStart; y < height - innerStart; y++)
                {
                    ProcessPixel(x, y);                   // Left inner band
                    ProcessPixel(width - 1 - x, y);       // Right inner band
                }
            }

            int maxCount = 0;
            int modeBin = -1;
            for (int i = 0; i < 32768; i++)
            {
                if (histogram[i] > maxCount)
                {
                    maxCount = histogram[i];
                    modeBin = i;
                }
            }

            if (modeBin >= 0 && maxCount > 0)
            {
                byte avgB = (byte)Math.Clamp(sumB[modeBin] / maxCount, 0, 255);
                byte avgG = (byte)Math.Clamp(sumG[modeBin] / maxCount, 0, 255);
                byte avgR = (byte)Math.Clamp(sumR[modeBin] / maxCount, 0, 255);
                return new Vec4b(avgB, avgG, avgR, 255);
            }

            return new Vec4b(200, 200, 200, 255);
        }

        /// <summary>
        /// Cleans cut marks, die-cut border nicks, and scanner edge artifacts along the perimeter of the chit
        /// by replacing or inpainting offending pixels with the counter's background color.
        /// </summary>
        public static Mat ApplyEdgeCleanup(
            Mat src,
            int depth,
            int tolerance,
            EdgeCleanMode mode,
            Vec4b? targetBgColor = null,
            bool preserveTransparentCorners = true)
        {
            if (src == null || src.IsDisposed || src.Width < MinDimension || src.Height < MinDimension)
            {
                throw new ArgumentException("Invalid source image matrix for edge cleanup.", nameof(src));
            }

            int d = Math.Clamp(depth, 1, Math.Min(src.Width / 3, src.Height / 3));
            int tol = Math.Clamp(tolerance, 5, 200);
            int tolSq = tol * tol;

            Vec4b bg = targetBgColor ?? SampleDominantBackgroundColor(src, d);

            Mat dst = src.Clone();
            int width = dst.Width;
            int height = dst.Height;

            bool IsInOuterBand(int x, int y)
            {
                return x < d || x >= width - d || y < d || y >= height - d;
            }

            if (mode == EdgeCleanMode.SolidFlood)
            {
                var indexer = dst.GetGenericIndexer<Vec4b>();
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (IsInOuterBand(x, y))
                        {
                            Vec4b cur = indexer[y, x];
                            if (preserveTransparentCorners && cur.Item3 == 0) continue;
                            indexer[y, x] = bg;
                        }
                    }
                }
                return dst;
            }
            else if (mode == EdgeCleanMode.TargetedReplace)
            {
                var indexer = dst.GetGenericIndexer<Vec4b>();
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (IsInOuterBand(x, y))
                        {
                            Vec4b cur = indexer[y, x];
                            if (preserveTransparentCorners && cur.Item3 == 0) continue;

                            int db = cur.Item0 - bg.Item0;
                            int dg = cur.Item1 - bg.Item1;
                            int dr = cur.Item2 - bg.Item2;
                            int distSq = (db * db) + (dg * dg) + (dr * dr);

                            if (distSq > tolSq)
                            {
                                indexer[y, x] = bg;
                            }
                        }
                    }
                }
                return dst;
            }
            else if (mode == EdgeCleanMode.SmartInpaint)
            {
                using Mat mask = new Mat(new OpenCvSharp.Size(width, height), MatType.CV_8UC1, new Scalar(0));
                var maskIndexer = mask.GetGenericIndexer<byte>();
                var dstIndexer = dst.GetGenericIndexer<Vec4b>();

                bool hasDefects = false;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (IsInOuterBand(x, y))
                        {
                            Vec4b cur = dstIndexer[y, x];
                            if (preserveTransparentCorners && cur.Item3 == 0) continue;

                            int db = cur.Item0 - bg.Item0;
                            int dg = cur.Item1 - bg.Item1;
                            int dr = cur.Item2 - bg.Item2;
                            int distSq = (db * db) + (dg * dg) + (dr * dr);

                            if (distSq > tolSq)
                            {
                                maskIndexer[y, x] = 255;
                                hasDefects = true;
                            }
                        }
                    }
                }

                if (!hasDefects)
                {
                    return dst;
                }

                using Mat bgr = new Mat();
                using Mat alpha = new Mat();
                Cv2.CvtColor(dst, bgr, ColorConversionCodes.BGRA2BGR);
                Cv2.ExtractChannel(dst, alpha, 3);

                using Mat inpaintedBgr = new Mat();
                Cv2.Inpaint(bgr, mask, inpaintedBgr, inpaintRadius: 3, InpaintMethod.Telea);

                using Mat chB = new Mat();
                using Mat chG = new Mat();
                using Mat chR = new Mat();
                Cv2.ExtractChannel(inpaintedBgr, chB, 0);
                Cv2.ExtractChannel(inpaintedBgr, chG, 1);
                Cv2.ExtractChannel(inpaintedBgr, chR, 2);

                Mat result = new Mat();
                Cv2.Merge(new[] { chB, chG, chR, alpha }, result);
                dst.Dispose();
                return result;
            }

            return dst;
        }
    }
}
