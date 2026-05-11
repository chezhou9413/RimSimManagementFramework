using System.Collections.Generic;
using System.Runtime.Serialization;
using UnityEngine;
using Verse;

namespace SimManagementLib.Pojo
{
    /// <summary>
    /// 负责保存本地招牌图库索引数据。
    /// </summary>
    [DataContract]
    public sealed class SignImageLibraryData
    {
        [DataMember]
        public int version = 1;

        [DataMember]
        public List<SignImageRecord> images = new List<SignImageRecord>();
    }

    /// <summary>
    /// 负责描述一个已经导入本地图库的招牌图片。
    /// </summary>
    [DataContract]
    public sealed class SignImageRecord
    {
        [DataMember]
        public string imageId = "";

        [DataMember]
        public string label = "";

        [DataMember]
        public string fileName = "";

        [DataMember]
        public int width;

        [DataMember]
        public int height;

        [DataMember]
        public long createdAtTicks;
    }

    /// <summary>
    /// 负责保存招牌单个可编辑面的图层列表。
    /// </summary>
    public sealed class SignFaceData : IExposable
    {
        public List<SignImageLayerData> layers = new List<SignImageLayerData>();

        /// <summary>
        /// 负责保存和读取当前面的图层列表。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Collections.Look(ref layers, "layers", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && layers == null)
                layers = new List<SignImageLayerData>();
        }

        /// <summary>
        /// 负责创建当前面的深拷贝，供编辑器草稿使用。
        /// </summary>
        public SignFaceData Clone()
        {
            SignFaceData clone = new SignFaceData();
            for (int i = 0; i < layers.Count; i++)
            {
                if (layers[i] != null)
                    clone.layers.Add(layers[i].Clone());
            }

            return clone;
        }
    }

    /// <summary>
    /// 负责保存招牌图片图层的引用和变换参数。
    /// </summary>
    public sealed class SignImageLayerData : IExposable
    {
        public string imageId = "";
        public string label = "";
        public bool enabled = true;
        public float x;
        public float y;
        public float scaleX = 1f;
        public float scaleY = 1f;
        public float angle;
        public int drawOrder;

        /// <summary>
        /// 负责保存和读取单个图片图层的轻量数据。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref imageId, "imageId", "");
            Scribe_Values.Look(ref label, "label", "");
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref x, "x", 0f);
            Scribe_Values.Look(ref y, "y", 0f);
            Scribe_Values.Look(ref scaleX, "scaleX", 1f);
            Scribe_Values.Look(ref scaleY, "scaleY", 1f);
            Scribe_Values.Look(ref angle, "angle", 0f);
            Scribe_Values.Look(ref drawOrder, "drawOrder", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (imageId == null) imageId = "";
                if (label == null) label = "";
                scaleX = Mathf.Clamp(scaleX, 0.05f, 4f);
                scaleY = Mathf.Clamp(scaleY, 0.05f, 4f);
            }
        }

        /// <summary>
        /// 负责创建当前图层的深拷贝，避免编辑草稿直接改动建筑数据。
        /// </summary>
        public SignImageLayerData Clone()
        {
            return new SignImageLayerData
            {
                imageId = imageId ?? "",
                label = label ?? "",
                enabled = enabled,
                x = x,
                y = y,
                scaleX = scaleX,
                scaleY = scaleY,
                angle = angle,
                drawOrder = drawOrder
            };
        }
    }

    /// <summary>
    /// 负责承载一个招牌配置的分享包数据。
    /// </summary>
    [DataContract]
    public sealed class SignSharePackage
    {
        [DataMember]
        public int version = 1;

        [DataMember]
        public List<SignShareImageRecord> images = new List<SignShareImageRecord>();

        [DataMember]
        public List<SignShareLayerRecord> southLayers = new List<SignShareLayerRecord>();

        [DataMember]
        public List<SignShareLayerRecord> eastLayers = new List<SignShareLayerRecord>();

        [DataMember]
        public List<SignShareLayerRecord> northLayers = new List<SignShareLayerRecord>();
    }

    /// <summary>
    /// 负责承载分享包中的图片内容。
    /// </summary>
    [DataContract]
    public sealed class SignShareImageRecord
    {
        [DataMember]
        public string imageId = "";

        [DataMember]
        public string label = "";

        [DataMember]
        public int width;

        [DataMember]
        public int height;

        [DataMember]
        public string pngBase64 = "";
    }

    /// <summary>
    /// 负责承载分享包中的图层参数。
    /// </summary>
    [DataContract]
    public sealed class SignShareLayerRecord
    {
        [DataMember]
        public string imageId = "";

        [DataMember]
        public string label = "";

        [DataMember]
        public bool enabled = true;

        [DataMember]
        public float x;

        [DataMember]
        public float y;

        [DataMember]
        public float scaleX = 1f;

        [DataMember]
        public float scaleY = 1f;

        [DataMember]
        public float angle;

        [DataMember]
        public int drawOrder;
    }
}
