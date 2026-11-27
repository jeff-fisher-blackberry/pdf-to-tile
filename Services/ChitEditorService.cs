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
    /// Shape mask for Clone Stamp tool.
    /// </summary>
    public enum CloneStampShape
    {
        Circle,
        Square
    }

    /// <summary>
    /// High-performance wargame chit image editing algorithms ported and adapted from GTL Text Editor.
    /// Operates on 4-channel BGRA (CV_8UC4) matrices with minimum dimension guards.
    /// </summary>
    public static class ChitEditorService
    {
        private const int MinDimension = 8;

        /// <summary>
        /// Computes the square dimension as min(targetWidth, targetHeight) clamped to >= MinDimension (8).
        /// </summary>
        public static int CalculateSquareDimension(int targetWidth, int targetHeight)
        {
            return Math.Max(MinDimension, Math.Min(targetWidth, targetHeight));
        }

        /// <summary>
        /// Calculates the default 4% perimeter bevel size based on the smaller dimension of the chit,
        /// clamped to at least 1px and at most 1/3 of the chit's primary dimension.
        /// </summary>
        public static int CalculateDefaultBevelSize(int width, int height)
        {
            if (width < MinDimension || height < MinDimension)
            {
                return 4;
            }

            int baseDim = Math.Min(width, height);
            int maxLimit = Math.Max(1, baseDim / 3);
            int bevel = (int)Math.Round(baseDim * 0.04);
            return Math.Clamp(bevel, 1, maxLimit);
        }

        /// <summary>
        /// Calculates the default 5% clone stamp size based on the smaller dimension of the chit,
        /// clamped to at least 1px and at most 40px (guarding width >= 8 && height >= 8).
        /// </summary>
        public static int CalculateDefaultCloneStampSize(int width, int height)
        {
            if (width < MinDimension || height < MinDimension)
            {
                return 4;
            }

            int baseDim = Math.Min(width, height);
            int maxLimit = Math.Max(1, Math.Min(40, baseDim / 4));
            int size = (int)Math.Round(baseDim * 0.05);
            return Math.Clamp(size, 1, maxLimit);
        }

        /// <summary>
        /// Applies a directional 3D raised-edge gradient bevel (highlight from top-left, shadow on bottom-right)
        /// around the perimeter of the chit using 45° mitered edge classification.
        /// Preserves existing transparent pixels (A = 0 or matching transparentColor) so corner clips are not overwritten.
        /// </summary>
        public static Mat ApplyBevel(Mat src, int borderSize, Vec4b? highlightColor = null, Vec4b? shadowColor = null, Vec4b? transparentColor = null)
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

            // Process only the perimeter band [0..b) on all four borders
            for (int y = 0; y < height; y++)
            {
                int dt = y;
                int db = height - 1 - y;
                bool isVertBorder = (dt < b || db < b);

                for (int x = 0; x < width; x++)
                {
                    int dl = x;
                    int dr = width - 1 - x;

                    // If not in vertical border and not in horizontal border, skip interior
                    if (!isVertBorder && dl >= b && dr >= b)
                    {
                        // Fast skip across the interior row
                        x = width - b - 1;
                        continue;
                    }

                    int minD = Math.Min(Math.Min(dt, db), Math.Min(dl, dr));
                    if (minD >= b) continue;

                    Vec4b p = indexer[y, x];
                    if (p.Item3 == 0) continue; // Skip fully transparent pixels (preserves corner clips)
                    if (transparentColor.HasValue &&
                        p.Item0 == transparentColor.Value.Item0 &&
                        p.Item1 == transparentColor.Value.Item1 &&
                        p.Item2 == transparentColor.Value.Item2 &&
                        p.Item3 == transparentColor.Value.Item3)
                    {
                        continue; // Skip transparent key pixels (preserves corner clips)
                    }

                    Vec4b targetColor;

                    // 45° Miter classification based on distance to nearest edges
                    bool isUpperHalf = (dt <= db);
                    bool isLeftHalf = (dl <= dr);

                    if (isUpperHalf && isLeftHalf)
                    {
                        // Top-Left quadrant: both Top and Left are highlight edges
                        targetColor = hi;
                    }
                    else if (!isUpperHalf && !isLeftHalf)
                    {
                        // Bottom-Right quadrant: both Bottom and Right are shadow edges
                        targetColor = sh;
                    }
                    else if (isUpperHalf && !isLeftHalf)
                    {
                        // Top-Right quadrant: Top (highlight) meets Right (shadow) along 45° miter (dt == dr)
                        if (dt < dr)
                        {
                            targetColor = hi;
                        }
                        else if (dr < dt)
                        {
                            targetColor = sh;
                        }
                        else
                        {
                            // Exact 45° diagonal seam: neutral transition
                            targetColor = new Vec4b(
                                (byte)((hi.Item0 + sh.Item0) / 2),
                                (byte)((hi.Item1 + sh.Item1) / 2),
                                (byte)((hi.Item2 + sh.Item2) / 2),
                                255);
                        }
                    }
                    else
                    {
                        // Bottom-Left quadrant: Left (highlight) meets Bottom (shadow) along 45° miter (dl == db)
                        if (dl < db)
                        {
                            targetColor = hi;
                        }
                        else if (db < dl)
                        {
                            targetColor = sh;
                        }
                        else
                        {
                            // Exact 45° diagonal seam: neutral transition
                            targetColor = new Vec4b(
                                (byte)((hi.Item0 + sh.Item0) / 2),
                                (byte)((hi.Item1 + sh.Item1) / 2),
                                (byte)((hi.Item2 + sh.Item2) / 2),
                                255);
                        }
                    }

                    float t = 1.0f - ((float)minD / b);
                    float strength = t * 0.70f;

                    byte nb = (byte)Math.Clamp(Math.Round(p.Item0 * (1f - strength) + targetColor.Item0 * strength), 0, 255);
                    byte ng = (byte)Math.Clamp(Math.Round(p.Item1 * (1f - strength) + targetColor.Item1 * strength), 0, 255);
                    byte nr = (byte)Math.Clamp(Math.Round(p.Item2 * (1f - strength) + targetColor.Item2 * strength), 0, 255);

                    indexer[y, x] = new Vec4b(nb, ng, nr, p.Item3); // Preserve original alpha
                }
            }

            return dst;
        }

        /// <summary>
        /// Clips/rounds the 4 corner pixels of the chit along circular arcs of radius cornerSize
        /// using the specified transparent color (defaulting to cyan BGRA #00FFFF or true transparency).
        /// </summary>
        public static Mat ApplyCornerClip(Mat src, int cornerSize, Vec4b? clipColor = null)
        {
            if (src == null || src.IsDisposed || src.Width < MinDimension || src.Height < MinDimension)
            {
                throw new ArgumentException("Invalid source image matrix for corner clip.", nameof(src));
            }

            int r = Math.Clamp(cornerSize, 1, Math.Min(src.Width / 2, src.Height / 2));
            Mat dst = src.Clone();
            int width = dst.Width;
            int height = dst.Height;
            // Default to cyan (B=255, G=255, R=0, A=255) if clipColor is not specified
            Vec4b clr = clipColor ?? new Vec4b(255, 255, 0, 255);

            var indexer = dst.GetGenericIndexer<Vec4b>();
            double rSq = (double)r * r;

            for (int y = 0; y < r; y++)
            {
                double dy = (r - 0.5) - y;
                double dySq = dy * dy;

                for (int x = 0; x < r; x++)
                {
                    double dx = (r - 0.5) - x;
                    if (dx * dx + dySq > rSq)
                    {
                        // Upper-Left corner
                        indexer[y, x] = clr;

                        // Upper-Right corner
                        indexer[y, width - 1 - x] = clr;

                        // Lower-Left corner
                        indexer[height - 1 - y, x] = clr;

                        // Lower-Right corner
                        indexer[height - 1 - y, width - 1 - x] = clr;
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
        /// Resamples/scales the chit image directly to fit targetW × targetH edge-to-edge.
        /// Uses Area interpolation when downsampling on both dimensions to prevent aliasing,
        /// or Lanczos4 interpolation whenever either dimension expands to prevent nearest-neighbor artifacts.
        /// </summary>
        public static Mat ApplyScale(Mat src, int targetW, int targetH)
        {
            if (src == null || src.IsDisposed)
            {
                throw new ArgumentException("Invalid source matrix for scale.", nameof(src));
            }

            int tw = Math.Max(MinDimension, targetW);
            int th = Math.Max(MinDimension, targetH);

            if (src.Width == tw && src.Height == th)
            {
                return src.Clone();
            }

            // Downsampling on both axes uses Area interpolation for alias-free reduction.
            // If either dimension expands (upsampling or mixed axes), use Lanczos4 for smooth bicubic interpolation.
            InterpolationFlags interp = (tw <= src.Width && th <= src.Height)
                ? InterpolationFlags.Area
                : InterpolationFlags.Lanczos4;

            Mat dst = new Mat();
            Cv2.Resize(src, dst, new OpenCvSharp.Size(tw, th), 0, 0, interp);
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
        /// Shifts/bumps the chit image content by (dx, dy) pixels inside the canvas,
        /// filling newly exposed edge strips with the transparent color or specified key.
        /// Positive dx shifts right; positive dy shifts down.
        /// </summary>
        public static Mat ApplyBump(Mat src, int dx, int dy, Vec4b? fillColor = null)
        {
            if (src == null || src.IsDisposed)
            {
                throw new ArgumentException("Invalid source matrix for bump.", nameof(src));
            }

            if (dx == 0 && dy == 0)
            {
                return src.Clone();
            }

            int width = src.Width;
            int height = src.Height;

            Vec4b fill = fillColor ?? new Vec4b(0, 0, 0, 0);
            Scalar fillScalar = new Scalar(fill.Item0, fill.Item1, fill.Item2, fill.Item3);

            Mat dst = new Mat(src.Size(), MatType.CV_8UC4, fillScalar);

            int copyW = width - Math.Abs(dx);
            int copyH = height - Math.Abs(dy);

            if (copyW > 0 && copyH > 0)
            {
                int srcX = Math.Max(0, -dx);
                int srcY = Math.Max(0, -dy);

                int dstX = Math.Max(0, dx);
                int dstY = Math.Max(0, dy);

                Rect srcRoi = new Rect(srcX, srcY, copyW, copyH);
                Rect dstRoi = new Rect(dstX, dstY, copyW, copyH);

                using var srcSub = new Mat(src, srcRoi);
                using var dstSub = new Mat(dst, dstRoi);
                srcSub.CopyTo(dstSub);
            }

            return dst;
        }

        /// <summary>
        /// Rotates the tile 90 degrees clockwise or counter-clockwise.
        /// Guards against invalid dimensions (Width >= 8 && Height >= 8) and preserves CV_8UC4 format.
        /// </summary>
        public static Mat Rotate(Mat src, bool clockwise)
        {
            if (src == null || src.IsDisposed || src.Width < 8 || src.Height < 8)
            {
                throw new ArgumentException("Invalid Mat for 90-degree tile rotation.", nameof(src));
            }

            var dst = new Mat();
            Cv2.Rotate(src, dst, clockwise ? RotateFlags.Rotate90Clockwise : RotateFlags.Rotate90Counterclockwise);
            return dst;
        }

        /// <summary>
        /// Applies an edge-preserving bilateral filter to smooth background paper grain and compression artifacts
        /// while keeping fine text and unit line work crisp. Operates on 4-channel BGRA matrices (CV_8UC4)
        /// while preserving transparent pixels and transparent key color pixels without halo or edge bleed.
        /// </summary>
        public static Mat ApplySmoothBackground(Mat src, int d, double sigmaColor, double sigmaSpace, Vec4b? transparentColor = null)
        {
            if (src == null || src.IsDisposed || src.Width < MinDimension || src.Height < MinDimension)
            {
                throw new ArgumentException("Invalid source image matrix for smooth background operation.", nameof(src));
            }

            int clampedD = Math.Clamp(d, 1, Math.Min(src.Width / 2, src.Height / 2));
            if (clampedD <= 0) clampedD = 5;

            using Mat bgr = new Mat();
            using Mat alpha = new Mat();
            Cv2.CvtColor(src, bgr, ColorConversionCodes.BGRA2BGR);
            Cv2.ExtractChannel(src, alpha, 3);

            using Mat filteredBgr = new Mat();
            Cv2.BilateralFilter(bgr, filteredBgr, clampedD, sigmaColor, sigmaSpace);

            using Mat chB = new Mat();
            using Mat chG = new Mat();
            using Mat chR = new Mat();
            Cv2.ExtractChannel(filteredBgr, chB, 0);
            Cv2.ExtractChannel(filteredBgr, chG, 1);
            Cv2.ExtractChannel(filteredBgr, chR, 2);

            Mat result = new Mat();
            Cv2.Merge(new[] { chB, chG, chR, alpha }, result);

            // Restore any transparent pixels (A == 0) or transparent key color pixels
            // so clipped/beveled corners do not bleed or soften
            var srcIdx = src.GetGenericIndexer<Vec4b>();
            var resIdx = result.GetGenericIndexer<Vec4b>();
            int width = src.Width;
            int height = src.Height;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vec4b orig = srcIdx[y, x];
                    if (orig.Item3 == 0)
                    {
                        resIdx[y, x] = orig;
                    }
                    else if (transparentColor.HasValue &&
                             orig.Item0 == transparentColor.Value.Item0 &&
                             orig.Item1 == transparentColor.Value.Item1 &&
                             orig.Item2 == transparentColor.Value.Item2 &&
                             orig.Item3 == transparentColor.Value.Item3)
                    {
                        resIdx[y, x] = orig;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Samples an inner perimeter band (depth+1 to depth+4 px) to statistically determine the modal
        /// (dominant) counter background color, ignoring text, numbers, and transparency.
        /// </summary>
        public static Vec4b SampleDominantBackgroundColor(Mat src, int depth = 2, Vec4b? transparentColor = null)
        {
            if (src == null || src.IsDisposed || src.Width < MinDimension || src.Height < MinDimension)
            {
                return new Vec4b(200, 200, 200, 255);
            }

            int width = src.Width;
            int height = src.Height;

            int maxDim = Math.Min(width, height);
            int innerStart = Math.Clamp(depth + 1, 1, Math.Max(1, maxDim / 2 - 2));
            int innerEnd = Math.Clamp(Math.Max(innerStart, depth + 4), innerStart, Math.Max(innerStart, maxDim / 2 - 1));

            var indexer = src.GetGenericIndexer<Vec4b>();

            // 3D quantized histogram: 32 bins per channel (5 bits: >> 3) -> 32,768 bins
            int[] histogram = new int[32768];
            long[] sumB = new long[32768];
            long[] sumG = new long[32768];
            long[] sumR = new long[32768];

            void ProcessPixel(int x, int y)
            {
                if (x < 0 || x >= width || y < 0 || y >= height) return;
                Vec4b p = indexer[y, x];
                if (p.Item3 == 0) return; // Skip transparent pixels
                if (transparentColor.HasValue &&
                    p.Item0 == transparentColor.Value.Item0 &&
                    p.Item1 == transparentColor.Value.Item1 &&
                    p.Item2 == transparentColor.Value.Item2 &&
                    p.Item3 == transparentColor.Value.Item3)
                {
                    return; // Skip transparent key pixels
                }

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
        /// Samples the dominant background color along the inner perimeter of the chit.
        /// </summary>
        public static Vec4b SampleDominantBackgroundColor(Mat src, int topDepth, int bottomDepth, int leftDepth, int rightDepth, Vec4b? transparentColor = null)
        {
            int maxDepth = Math.Max(Math.Max(topDepth, bottomDepth), Math.Max(leftDepth, rightDepth));
            return SampleDominantBackgroundColor(src, Math.Max(1, maxDepth), transparentColor);
        }

        /// <summary>
        /// Cleans cut marks, die-cut border nicks, and scanner edge artifacts along the perimeter of the chit
        /// by replacing or inpainting offending pixels with the counter's background color, using independent per-side depths.
        /// Preserves existing corner clips (A = 0 or matching transparentColor).
        /// </summary>
        public static Mat ApplyEdgeCleanup(
            Mat src,
            int topDepth,
            int bottomDepth,
            int leftDepth,
            int rightDepth,
            int tolerance,
            EdgeCleanMode mode,
            Vec4b? targetBgColor = null,
            bool preserveTransparentCorners = true,
            Vec4b? transparentColor = null)
        {
            if (src == null || src.IsDisposed || src.Width < MinDimension || src.Height < MinDimension)
            {
                throw new ArgumentException("Invalid source image matrix for edge cleanup.", nameof(src));
            }

            int width = src.Width;
            int height = src.Height;

            int maxVert = Math.Max(0, height / 2);
            int maxHoriz = Math.Max(0, width / 2);

            int topD = Math.Clamp(topDepth, 0, maxVert);
            int bottomD = Math.Clamp(bottomDepth, 0, maxVert);
            if (topD + bottomD >= height)
            {
                bottomD = Math.Max(0, height - topD - 1);
            }

            int leftD = Math.Clamp(leftDepth, 0, maxHoriz);
            int rightD = Math.Clamp(rightDepth, 0, maxHoriz);
            if (leftD + rightD >= width)
            {
                rightD = Math.Max(0, width - leftD - 1);
            }

            int maxD = Math.Max(Math.Max(topD, bottomD), Math.Max(leftD, rightD));
            int tol = Math.Clamp(tolerance, 5, 200);
            int tolSq = tol * tol;

            Vec4b bg = targetBgColor ?? SampleDominantBackgroundColor(src, Math.Max(1, maxD), transparentColor);

            Mat dst = src.Clone();

            if (maxD == 0)
            {
                return dst;
            }

            bool IsTransparentCornerPixel(Vec4b p)
            {
                if (p.Item3 == 0) return true;
                if (transparentColor.HasValue &&
                    p.Item0 == transparentColor.Value.Item0 &&
                    p.Item1 == transparentColor.Value.Item1 &&
                    p.Item2 == transparentColor.Value.Item2 &&
                    p.Item3 == transparentColor.Value.Item3)
                {
                    return true;
                }
                return false;
            }

            bool IsInOuterBand(int x, int y)
            {
                return (topD > 0 && y < topD)
                    || (bottomD > 0 && y >= height - bottomD)
                    || (leftD > 0 && x < leftD)
                    || (rightD > 0 && x >= width - rightD);
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
                            if (preserveTransparentCorners && IsTransparentCornerPixel(cur)) continue;
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
                            if (preserveTransparentCorners && IsTransparentCornerPixel(cur)) continue;

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
                            if (preserveTransparentCorners && IsTransparentCornerPixel(cur)) continue;

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
                Cv2.Inpaint(bgr, mask, inpaintedBgr, inpaintRadius: Math.Max(1.0, (double)Math.Max(1, maxD)), InpaintMethod.Telea);

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

        /// <summary>
        /// Cleans cut marks, die-cut border nicks, and scanner edge artifacts along the perimeter of the chit
        /// by replacing or inpainting offending pixels with the counter's background color, using a uniform depth for all sides.
        /// Preserves existing corner clips (A = 0 or matching transparentColor).
        /// </summary>
        public static Mat ApplyEdgeCleanup(
            Mat src,
            int depth,
            int tolerance,
            EdgeCleanMode mode,
            Vec4b? targetBgColor = null,
            bool preserveTransparentCorners = true,
            Vec4b? transparentColor = null)
        {
            return ApplyEdgeCleanup(src, depth, depth, depth, depth, tolerance, mode, targetBgColor, preserveTransparentCorners, transparentColor);
        }

        /// <summary>
        /// Stamps a circular or square region of pixels from source offset (targetCenterX + deltaX, targetCenterY + deltaY)
        /// onto target (targetCenterX, targetCenterY) with boundary guards.
        /// Operates directly in-place on the 4-channel BGRA (CV_8UC4) matrix.
        /// </summary>
        public static bool ApplyCloneStamp(
            Mat targetMat,
            int targetCenterX,
            int targetCenterY,
            int deltaX,
            int deltaY,
            int radius,
            CloneStampShape shape = CloneStampShape.Circle)
        {
            if (targetMat == null || targetMat.IsDisposed || targetMat.Width < MinDimension || targetMat.Height < MinDimension)
            {
                return false;
            }

            int width = targetMat.Width;
            int height = targetMat.Height;
            int r = Math.Clamp(radius, 1, Math.Min(width, height));
            int rSq = r * r;

            var indexer = targetMat.GetGenericIndexer<Vec4b>();
            bool modified = false;

            for (int dy = -r; dy <= r; dy++)
            {
                int dstY = targetCenterY + dy;
                if (dstY < 0 || dstY >= height) continue;

                int srcY = dstY + deltaY;
                if (srcY < 0 || srcY >= height) continue;

                for (int dx = -r; dx <= r; dx++)
                {
                    if (shape == CloneStampShape.Circle && (dx * dx + dy * dy > rSq))
                    {
                        continue;
                    }

                    int dstX = targetCenterX + dx;
                    if (dstX < 0 || dstX >= width) continue;

                    int srcX = dstX + deltaX;
                    if (srcX < 0 || srcX >= width) continue;

                    indexer[dstY, dstX] = indexer[srcY, srcX];
                    modified = true;
                }
            }

            return modified;
        }

        /// <summary>
        /// Applies an anchored (fixed source) clone stamp, copying the captured square or circular patch centered
        /// at (anchorCenterX, anchorCenterY) in sourceMat directly onto (targetCenterX, targetCenterY) in targetMat.
        /// </summary>
        public static bool ApplyCloneStampAnchored(
            Mat targetMat,
            int targetCenterX,
            int targetCenterY,
            int anchorCenterX,
            int anchorCenterY,
            Mat sourceMat,
            int radius,
            CloneStampShape shape = CloneStampShape.Circle)
        {
            if (targetMat == null || targetMat.IsDisposed || targetMat.Width < MinDimension || targetMat.Height < MinDimension)
            {
                return false;
            }
            if (sourceMat == null || sourceMat.IsDisposed || sourceMat.Width < MinDimension || sourceMat.Height < MinDimension)
            {
                return false;
            }

            int targetW = targetMat.Width;
            int targetH = targetMat.Height;
            int sourceW = sourceMat.Width;
            int sourceH = sourceMat.Height;

            int r = Math.Clamp(radius, 1, Math.Min(targetW, targetH));
            int rSq = r * r;

            var targetIndexer = targetMat.GetGenericIndexer<Vec4b>();
            var sourceIndexer = sourceMat.GetGenericIndexer<Vec4b>();
            bool modified = false;

            for (int dy = -r; dy <= r; dy++)
            {
                int dstY = targetCenterY + dy;
                if (dstY < 0 || dstY >= targetH) continue;

                int srcY = anchorCenterY + dy;
                if (srcY < 0 || srcY >= sourceH) continue;

                for (int dx = -r; dx <= r; dx++)
                {
                    if (shape == CloneStampShape.Circle && (dx * dx + dy * dy > rSq))
                    {
                        continue;
                    }

                    int dstX = targetCenterX + dx;
                    if (dstX < 0 || dstX >= targetW) continue;

                    int srcX = anchorCenterX + dx;
                    if (srcX < 0 || srcX >= sourceW) continue;

                    targetIndexer[dstY, dstX] = sourceIndexer[srcY, srcX];
                    modified = true;
                }
            }

            return modified;
        }
    }
}
