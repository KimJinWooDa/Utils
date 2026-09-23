using System;

namespace TelleR.ConcaveCollider
{
    /// <summary>Auto/프리셋을 풀어낸 실제 수치.</summary>
    internal sealed class ResolvedSettings
    {
        public ConcaveQuality Quality;
        public bool AutoPieces;
        public int VoxelTarget;
        public int MaxAxisVoxels;
        public double Tolerance;
        public int MaxPieces;
        public int SplitFactor;
        public int SplitCap;
        public int CandidatesPerAxis;
        public double MinPieceFraction;
        public double DustFraction;
        public int MaxHullVertices;
        public bool FitBox, FitSphere, FitCapsule;
        public double FillThreshold;
        public double Padding;
        public bool MergeTight;
        public int MetricSamples;

        public int MaxBonePieces;
        public bool GroupBonesByRole;
        public string[] ExcludedBoneKeywords;
        public bool ClipAtJoints;
        public double MinBoneFraction;

        public const double TightMergeAir = 0.05;

        public static ResolvedSettings From(ConcaveColliderSettings source)
        {
            ConcaveColliderSettings s = source != null ? source.Clone() : new ConcaveColliderSettings();
            s.Validate();
            var r = new ResolvedSettings
            {
                GroupBonesByRole = s.groupBonesByRole,
                ExcludedBoneKeywords = s.ParseExcludedBoneKeywords(),
                ClipAtJoints = s.clipAtJoints
            };

            if (s.auto)
            {
                r.Quality = ConcaveQuality.Balanced;
                r.AutoPieces = true;
                r.MaxPieces = 8;
                r.Tolerance = 0.01;
                r.MinPieceFraction = 0.015;
                r.MaxHullVertices = 48;
                r.FitBox = r.FitSphere = r.FitCapsule = true;
                r.FillThreshold = 0.8;
                r.Padding = 0;
                r.MergeTight = true;
                r.MaxBonePieces = 10;
                r.MinBoneFraction = 0.03;
                ApplyQuality(r, ConcaveQuality.Balanced, 150000);
            }
            else
            {
                r.Quality = s.quality;
                r.MaxPieces = s.maxPieces;
                r.Tolerance = s.concavityTolerance;
                r.MinPieceFraction = s.minPieceVolumeFraction;
                r.MaxHullVertices = s.maxHullVertices;
                r.FitBox = s.fitBoxes;
                r.FitSphere = s.fitSpheres;
                r.FitCapsule = s.fitCapsules;
                r.FillThreshold = s.primitiveFillThreshold;
                r.Padding = s.padding;
                r.MergeTight = s.mergeTightPieces;
                r.MaxBonePieces = s.maxBonePieces;
                r.MinBoneFraction = s.minBoneVolumeFraction;
                ApplyQuality(r, s.quality, s.voxelResolution);
            }

            r.DustFraction = Math.Min(0.003, r.MinPieceFraction * 0.2);
            return r;
        }

        private static void ApplyQuality(ResolvedSettings r, ConcaveQuality quality, int voxelOverride)
        {
            switch (quality)
            {
                case ConcaveQuality.Fast:
                    r.VoxelTarget = 40000;
                    r.MaxAxisVoxels = 200;
                    r.SplitFactor = 2;
                    r.SplitCap = 32;
                    r.CandidatesPerAxis = 8;
                    r.MetricSamples = 16000;
                    break;
                case ConcaveQuality.Precise:
                    r.VoxelTarget = 250000;
                    r.MaxAxisVoxels = 400;
                    r.SplitFactor = 4;
                    r.SplitCap = 64;
                    r.CandidatesPerAxis = 16;
                    r.MetricSamples = 40000;
                    break;
                default:
                    r.VoxelTarget = 100000;
                    r.MaxAxisVoxels = 320;
                    r.SplitFactor = 3;
                    r.SplitCap = 48;
                    r.CandidatesPerAxis = 12;
                    r.MetricSamples = 30000;
                    break;
            }

            if (voxelOverride > 0) r.VoxelTarget = Math.Max(1000, voxelOverride);
        }

        /// <summary>Auto 조각 수: 초기 오목도 비율(1 - 솔리드/껍질)이 클수록 많이.</summary>
        public void ResolveAutoPieces(double initialConcavityRatio)
        {
            if (!AutoPieces) return;
            MaxPieces = ConcaveMath.Clamp((int)Math.Round(4 + 12 * initialConcavityRatio), 4, 12);
        }

        public int SplitLimit => ConcaveMath.Clamp(MaxPieces * SplitFactor, MaxPieces + 2, SplitCap);
    }
}
