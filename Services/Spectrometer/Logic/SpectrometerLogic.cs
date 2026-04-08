using GD_ControlCenter_WPF.Models.Spectrometer;

namespace GD_ControlCenter_WPF.Services.Spectrometer.Logic
{
    public static class SpectrometerLogic
    {
        private static readonly object _stitchLock = new();

        public static SpectralData? PerformStitching(IEnumerable<SpectralData> dataCollection)
        {
            if (dataCollection == null || dataCollection.Count() < 2) return null;
            lock (_stitchLock)
            {
                var allPoints = dataCollection.SelectMany(d => d.Wavelengths.Zip(d.Intensities, (w, i) => new { w, i }))
                                              .OrderBy(p => p.w).ToList();
                return new SpectralData(allPoints.Select(p => p.w).ToArray(), allPoints.Select(p => p.i).ToArray(), "Combined_System");
            }
        }

        public static bool CheckSaturation(SpectralData data) => data.Intensities.Any(i => i > 65000);

        public static double GetActualPeakWavelength(SpectralData data, double targetWl, double tolerance)
        {
            if (data == null || data.Wavelengths.Length == 0) return targetWl;
            double maxI = -1; double bestWl = targetWl;
            for (int i = 0; i < data.Wavelengths.Length; i++)
            {
                double w = data.Wavelengths[i];
                if (Math.Abs(w - targetWl) <= tolerance && data.Intensities[i] > maxI)
                {
                    maxI = data.Intensities[i]; bestWl = w;
                }
            }
            return bestWl;
        }

        public static double GetIntensityAtWavelength(SpectralData data, double targetWl)
        {
            if (data == null || data.Wavelengths.Length == 0) return 0;
            return data.Intensities[Array.BinarySearch(data.Wavelengths, targetWl) < 0 ? ~Array.BinarySearch(data.Wavelengths, targetWl) : Array.BinarySearch(data.Wavelengths, targetWl)];
        }
    }
}