using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace YusufOkuducu.PcPsoEliminator
{
    [Serializable]
    public struct HarvestedVariantRecord
    {
        public string shaderName;
        public PassType passType;
        public string[] keywords;
        public string sourceAssetPath;
    }

    [CreateAssetMenu(
        fileName = "PsoWarmupManifest",
        menuName = "PC PSO Eliminator/PSO Warmup Manifest")]
    public sealed class PsoWarmupManifest : ScriptableObject
    {
        public ShaderVariantCollection variantCollection;
        public List<Material> hardwareDrawWarmupMaterials = new List<Material>();
        public List<HarvestedVariantRecord> harvestedRecords = new List<HarvestedVariantRecord>();
        public string lastHarvestTimestampUtc;
        public int uniqueShaderCount;
        public int uniqueMaterialCount;
    }
}
