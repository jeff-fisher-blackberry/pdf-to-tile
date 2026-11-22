using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace pdf_to_Tiles.Services
{
    /// <summary>
    /// Handles scan tilt detection and high-quality Lanczos4 rotational deskewing.
    /// Strictly manages native Mat allocations to prevent memory exhaustion on high-DPI scans.
    /// </summary>
    public static class DeskewService
    {
        /// <summary>
        /// Detects the rotational tilt angle of a counter sheet in degrees.
        /// Downscales to a 1000px proxy buffer and excludes the outer 5% periphery to prevent dark scanner-lid edges
        /// from misguiding the Hough line analysis.
        /// </summary>
        public static double DetectTiltAngle(Mat srcMat)
        {
            if (srcMat == null || srcMat.IsDisposed || srcMat.Width <= 0 || srcMat.Height <= 0)
            {
                return 0.0;
            }

            // Downsample to a lightweight proxy image (width ~ 1000px)
            double scale = srcMat.Width > 1000 ? 1000.0 / srcMat.Width : 1.0;
            int proxyW = Math.Max(100, (int)(srcMat.Width * scale));
            int proxyH = Math.Max(100, (int)(srcMat.Height * scale));

            using var proxy = new Mat();
            Cv2.Resize(srcMat, proxy, new Size(proxyW, proxyH), 0, 0, InterpolationFlags.Area);

            // Crop out outer 5% margin to ignore scanner lid shadows / page edges
            int marginX = (int)(proxyW * 0.05);
            int marginY = (int)(proxyH * 0.05);
            int innerW = proxyW - (2 * marginX);
            int innerH = proxyH - (2 * marginY);

            if (innerW <= 10 || innerH <= 10)
            {
                return 0.0;
            }

            using var croppedProxy = new Mat(proxy, new Rect(marginX, marginY, innerW, innerH));

            // Convert to grayscale
            using var gray = new Mat();
            if (croppedProxy.Channels() == 1)
            {
                croppedProxy.CopyTo(gray);
            }
            else
            {
                Cv2.CvtColor(croppedProxy, gray, ColorConversionCodes.BGR2GRAY);
            }

            // Mild blur to reduce paper grain noise
            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new Size(3, 3), 0);

            // Canny edge detection
            using var edges = new Mat();
            Cv2.Canny(blurred, edges, 50, 150);

            // Hough Line Probabilistic Transform
            LineSegmentPoint[] lines = Cv2.HoughLinesP(
                edges,
                rho: 1,
                theta: Math.PI / 180.0,
                threshold: 60,
                minLineLength: 60,
                maxLineGap: 10
            );

            if (lines == null || lines.Length == 0)
            {
                return 0.0;
            }

            var detectedAngles = new List<double>();

            foreach (var line in lines)
            {
                double dx = line.P2.X - line.P1.X;
                double dy = line.P2.Y - line.P1.Y;
                double angleDeg = Math.Atan2(dy, dx) * (180.0 / Math.PI);

                // Near horizontal lines (|angle| <= 15)
                if (Math.Abs(angleDeg) <= 15.0)
                {
                    detectedAngles.Add(angleDeg);
                }
                // Near vertical lines (|angle| between 75 and 105)
                else if (Math.Abs(angleDeg) >= 75.0 && Math.Abs(angleDeg) <= 105.0)
                {
                    double vAngle = angleDeg > 0 ? angleDeg - 90.0 : angleDeg + 90.0;
                    if (Math.Abs(vAngle) <= 15.0)
                    {
                        detectedAngles.Add(vAngle);
                    }
                }
            }

            if (detectedAngles.Count == 0)
            {
                return 0.0;
            }

            // Compute median angle
            detectedAngles.Sort();
            double medianAngle;
            int mid = detectedAngles.Count / 2;
            if (detectedAngles.Count % 2 != 0)
            {
                medianAngle = detectedAngles[mid];
            }
            else
            {
                medianAngle = (detectedAngles[mid - 1] + detectedAngles[mid]) / 2.0;
            }

            // Round to 2 decimal places
            return Math.Round(medianAngle, 2);
        }

        /// <summary>
        /// Rotates the image around its center by the specified angle in degrees using Lanczos4 interpolation
        /// and crisp white border padding.
        /// </summary>
        public static Mat RotateImage(Mat srcMat, double angleDegrees)
        {
            if (srcMat == null || srcMat.IsDisposed)
            {
                throw new ArgumentNullException(nameof(srcMat));
            }

            if (Math.Abs(angleDegrees) < 0.001)
            {
                return srcMat.Clone();
            }

            var center = new Point2f(srcMat.Width / 2f, srcMat.Height / 2f);
            using var rotMatrix = Cv2.GetRotationMatrix2D(center, angleDegrees, 1.0);

            var dst = new Mat();
            Cv2.WarpAffine(
                srcMat,
                dst,
                rotMatrix,
                srcMat.Size(),
                InterpolationFlags.Lanczos4,
                BorderTypes.Constant,
                new Scalar(255, 255, 255, 255) // Clean white border fill
            );

            return dst;
        }

        /// <summary>
        /// Encodes an OpenCV Mat into a frozen WPF BitmapSource for UI preview.
        /// </summary>
        public static BitmapSource MatToBitmapSource(Mat mat)
        {
            if (mat == null || mat.IsDisposed || mat.Width <= 0 || mat.Height <= 0)
            {
                throw new ArgumentException("Invalid Mat.", nameof(mat));
            }

            Cv2.ImEncode(".png", mat, out byte[] buffer);

            using var ms = new MemoryStream(buffer);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
    }
}
