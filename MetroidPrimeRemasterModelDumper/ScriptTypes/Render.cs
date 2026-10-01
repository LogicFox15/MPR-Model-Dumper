using AvaloniaToolbox.Core.IO;
using DKCTF;
using System.Numerics;
using static DKCTF.ROOM;
#nullable disable

namespace MetroidPrimeRemasterModelDumper.ScriptTypes
{
    /// <summary>
    /// Prime 4 Render component.
    ///
    /// The Render component is a property-list. The 0x948D7F67 property is a
    /// tagged/polymorphic property block: its first value is a 32-bit subtype
    /// hash, followed by that subtype's own property-list header/data.
    ///
    /// Unknown fields are retained as raw bytes so adding support for another
    /// Render subtype later does not require changing the outer parser.
    /// </summary>
    public class Render
    {
        public CommonObjectData commonObjectData = new CommonObjectData();

        // 0x948D7F67
        public RenderPropertiesBlock renderProperties;

        // 0xBFA1049E
        public uint renderTargetSceneHash;

        // 0x2B1317A0
        public ModelLightingData modelLightingData;

        public int unkInt;
        public bool unkBool1;
        public bool unkBool2;
        public bool unkBool3;
        public bool unkBool4;
        public bool unkBool5;
        public uint unkUint;

        // Preserve every outer property, including properties not yet mapped.
        public List<RenderRawProperty> properties = new List<RenderRawProperty>();

        public static Render Build(CGameObjectComponent component, ScriptDataEntity entity, SGOComponentInstanceData instanceData)
        {
            Render script = new Render();

            using (MemoryStream ms = new MemoryStream(entity.propertyData))
            using (FileReader br = new FileReader(ms))
            {
                BuildRenderProperties(br, script);
            }

            script.commonObjectData.originalComponent = component;
            script.commonObjectData.originalEntity = entity;
            script.commonObjectData.originalInstanceData = instanceData;

            return script;
        }

        public static Render prepTransform(Render render, ConstructedLayer parsed)
        {
            foreach (var prop in parsed.entityProperties)
            {
                foreach (var link in prop.linkGUIDs)
                {
                    if (link.ToString() == render.commonObjectData.originalInstanceData.id.ToString())
                    {
                        render.commonObjectData.entityProperties = prop;
                        return render;
                    }
                }
            }
            return render;
        }

        public static void BuildRenderProperties(FileReader reader, Render script)
        {
            if (reader.BaseStream.Position + 2 > reader.BaseStream.Length)
                return;

            ushort count = reader.ReadUInt16();

            for (int i = 0; i < count; i++)
            {
                if (reader.BaseStream.Position + 6 > reader.BaseStream.Length)
                    break;

                uint propertyId = reader.ReadUInt32();
                ushort propertySize = reader.ReadUInt16();

                byte[] propertyData = propertySize > 0
                    ? reader.ReadBytes(propertySize)
                    : Array.Empty<byte>();

                RenderRawProperty raw = new RenderRawProperty
                {
                    propertyId = propertyId,
                    propertySize = propertySize,
                    data = propertyData
                };

                script.properties.Add(raw);

                using MemoryStream ms = new MemoryStream(propertyData);
                using FileReader propertyReader = new FileReader(ms);

                switch (propertyId)
                {
                    case 0x948D7F67:                                                            // RenderProperties / tagged polymorphic block
                        script.renderProperties = RenderPropertiesBlock.Read(propertyReader);
                        break;
                    case 0xBFA1049E:                                                            // RenderTargetScene. 32-bit hash
                        if (propertySize >= 4)
                            script.renderTargetSceneHash = propertyReader.ReadUInt32();
                        break;
                    case 0x2B1317A0:                                                            // ModelLightingData
                        script.modelLightingData = ModelLightingData.Read(propertyReader);
                        break;
                    case 0x8C7307B4:
                        if (propertySize >= 4)
                            script.unkInt = unchecked((int)propertyReader.ReadUInt32());
                        break;
                    case 0x80EC379B:
                        if (propertySize >= 1)
                            script.unkBool1 = propertyReader.ReadByte() != 0;
                        break;
                    case 0x9B56A30E:
                        if (propertySize >= 1)
                            script.unkBool2 = propertyReader.ReadByte() != 0;
                        break;
                    case 0xBF414356:
                        if (propertySize >= 1)
                            script.unkBool3 = propertyReader.ReadByte() != 0;
                        break;
                    case 0xBFBAE144:
                        if (propertySize >= 4)
                            script.unkUint = propertyReader.ReadUInt32();
                        break;
                    case 0xB3DD2F87:
                        if (propertySize >= 1)
                            script.unkBool4 = propertyReader.ReadByte() != 0;
                        break;
                    case 0x4914F89A:
                        if (propertySize >= 1)
                            script.unkBool5 = propertyReader.ReadByte() != 0;
                        break;
                    default:
                        // Already consumed into propertyData.
                        break;
                }
            }
        }
    }

    public enum ERenderPropertiesType : uint
    {
        RenderAnimatedModel = 0xADA6B7DB,
        RenderCharacterModel = 0xF8771358,
        RenderMethodGameMode = 0x91F17031,
        RenderStaticModel = 0x13F5701A,
        RenderStaticModelArray = 0x26BE03FA,
        RenderTexture = 0x6FC13B67,
        RenderVertexAnimatedModel = 0xB93BFEB8
    }

    public enum ERenderTargetScene : uint
    {
        Unknown = 0xF6EEB57B,
        Orthographic = 0x90130085,
        Perspective = 0x986D766D
    }

    public class RenderRawProperty
    {
        public uint propertyId;
        public ushort propertySize;
        public byte[] data = Array.Empty<byte>();
    }

    /// <summary>
    /// The 0x948D7F67 RenderProperties value.
    /// The decomp reads a 32-bit subtype hash, then the subtype's field count
    /// and fields. This class deliberately keeps the field list generic.
    /// </summary>
    public class RenderPropertiesBlock
    {
        public uint typeHash;
        public ushort fieldCount;
        public List<RenderRawProperty> properties = new List<RenderRawProperty>();

        // Known resource/object IDs found inside documented Render subtypes.
        public List<CObjectId> assetIds = new List<CObjectId>();

        public ERenderPropertiesType? knownType
        {
            get
            {
                if (Enum.IsDefined(typeof(ERenderPropertiesType), typeHash))
                    return (ERenderPropertiesType)typeHash;

                return null;
            }
        }

        public static RenderPropertiesBlock Read(FileReader reader)
        {
            RenderPropertiesBlock block = new RenderPropertiesBlock();

            if (reader.BaseStream.Position + 4 > reader.BaseStream.Length)
                return block;

            block.typeHash = reader.ReadUInt32();

            // A null Property is represented by type hash 0 in the game code.
            if (block.typeHash == 0)
                return block;

            // FUN_71002edcb0 reads a u16 before handing the typed object to its
            // subtype deserializer. For these generated property containers,
            // this is the subtype field count.
            if (reader.BaseStream.Position + 2 > reader.BaseStream.Length)
                return block;

            block.fieldCount = reader.ReadUInt16();

            for (int i = 0; i < block.fieldCount; i++)
            {
                if (reader.BaseStream.Position + 6 > reader.BaseStream.Length)
                    break;

                uint propertyId = reader.ReadUInt32();
                ushort propertySize = reader.ReadUInt16();
                byte[] propertyData = propertySize > 0
                    ? reader.ReadBytes(propertySize)
                    : Array.Empty<byte>();

                block.properties.Add(new RenderRawProperty
                {
                    propertyId = propertyId,
                    propertySize = propertySize,
                    data = propertyData
                });

                ParseKnownSubtypeProperty(block, propertyId, propertyData);
            }

            return block;
        }

        private static void ParseKnownSubtypeProperty(
            RenderPropertiesBlock block,
            uint propertyId,
            byte[] propertyData)
        {
            // RenderAnimatedModel -> model/object resource ID.
            if (block.typeHash == (uint)ERenderPropertiesType.RenderAnimatedModel && propertyId == 0x6CD6726A)
            {
                TryReadObjectId(propertyData, block.assetIds);
                return;
            }

            // RenderStaticModel -> static model resource ID.
            if (block.typeHash == (uint)ERenderPropertiesType.RenderStaticModel && propertyId == 0xE8BDC12B)
            {
                TryReadObjectId(propertyData, block.assetIds);
                return;
            }

            // RenderStaticModelArray -> list<CObjectId>.
            // The cross-version format definition documents E2517798 as a
            // List<CObjectId>; generated lists use a u32 element count.
            if (block.typeHash == (uint)ERenderPropertiesType.RenderStaticModelArray && propertyId == 0xE2517798)
            {
                TryReadObjectIdList(propertyData, block.assetIds);
            }
        }

        private static void TryReadObjectId(byte[] data, List<CObjectId> destination)
        {
            try
            {
                using MemoryStream ms = new MemoryStream(data);
                using FileReader br = new FileReader(ms);
                destination.Add(br.ReadStruct<CObjectId>());
            }
            catch
            {
                // Keep the raw property instead of failing the whole component.
            }
        }

        private static void TryReadObjectIdList(byte[] data, List<CObjectId> destination)
        {
            try
            {
                using MemoryStream ms = new MemoryStream(data);
                using FileReader br = new FileReader(ms);

                if (br.BaseStream.Position + 4 > br.BaseStream.Length)
                    return;

                uint count = br.ReadUInt32();
                if (count > 0x100000)
                    return;

                for (uint i = 0; i < count; i++)
                {
                    destination.Add(br.ReadStruct<CObjectId>());
                }
            }
            catch
            {
                // Keep the raw property instead of failing the whole component.
            }
        }
    }

    /// <summary>
    /// Prime 4 ModelLightingData (0x2B1317A0).
    /// </summary>
    public class ModelLightingData
    {
        // 0x1AC9C6F6: FUN_710032f15c reads four u32 values.
        public uint color0Bits;
        public uint color1Bits;
        public uint color2Bits;
        public uint color3Bits;

        // Convenience interpretation of the same four raw values.
        public Vector4 colorAsFloat => new Vector4(
            BitConverter.UInt32BitsToSingle(color0Bits),
            BitConverter.UInt32BitsToSingle(color1Bits),
            BitConverter.UInt32BitsToSingle(color2Bits),
            BitConverter.UInt32BitsToSingle(color3Bits));

        // 0x1A89119F. The supplied helper reads a u32 + CObjectId,
        // skips a sized blob, then reads another trailing CObjectId.
        public ModelLightingDataLinkData linkData;

        public uint unkUint1;
        public bool unkBool1;
        public bool unkBool2;
        public CObjectId modelId;
        public bool unkBool3;

        public List<RenderRawProperty> properties = new List<RenderRawProperty>();

        public static ModelLightingData Read(FileReader reader)
        {
            ModelLightingData data = new ModelLightingData();

            if (reader.BaseStream.Position + 2 > reader.BaseStream.Length)
                return data;

            ushort count = reader.ReadUInt16();

            for (int i = 0; i < count; i++)
            {
                if (reader.BaseStream.Position + 6 > reader.BaseStream.Length)
                    break;

                uint propertyId = reader.ReadUInt32();
                ushort propertySize = reader.ReadUInt16();
                byte[] propertyData = propertySize > 0
                    ? reader.ReadBytes(propertySize)
                    : Array.Empty<byte>();

                data.properties.Add(new RenderRawProperty
                {
                    propertyId = propertyId,
                    propertySize = propertySize,
                    data = propertyData
                });

                using MemoryStream ms = new MemoryStream(propertyData);
                using FileReader propertyReader = new FileReader(ms);

                switch (propertyId)
                {
                    case 0x1AC9C6F6:
                        if (propertySize >= 16)
                        {
                            data.color0Bits = propertyReader.ReadUInt32();
                            data.color1Bits = propertyReader.ReadUInt32();
                            data.color2Bits = propertyReader.ReadUInt32();
                            data.color3Bits = propertyReader.ReadUInt32();
                        }
                        break;
                    case 0x1A89119F:
                        data.linkData = ModelLightingDataLinkData.Read(propertyReader);
                        break;
                    case 0xB65B2EF5:
                        if (propertySize >= 4)
                            data.unkUint1 = propertyReader.ReadUInt32();
                        break;
                    case 0x9AB5FD2B:
                        if (propertySize >= 1)
                            data.unkBool1 = propertyReader.ReadByte() != 0;
                        break;
                    case 0x0B639A58:
                        if (propertySize >= 1)
                            data.unkBool2 = propertyReader.ReadByte() != 0;
                        break;
                    case 0x7822056C:
                        try
                        {
                            data.modelId = propertyReader.ReadStruct<CObjectId>();
                        }
                        catch
                        {
                        }
                        break;
                    case 0x2331CBAA:
                        if (propertySize >= 1)
                            data.unkBool3 = propertyReader.ReadByte() != 0;
                        break;
                    default:
                        // Already consumed into propertyData.
                        break;
                }
            }

            return data;
        }
    }

    public class ModelLightingDataLinkData
    {
        public uint value;
        public CObjectId primaryObjectId;
        public byte[] skippedData = Array.Empty<byte>();
        public CObjectId trailingObjectId;

        public static ModelLightingDataLinkData Read(FileReader reader)
        {
            ModelLightingDataLinkData data = new ModelLightingDataLinkData();

            if (reader.BaseStream.Position + 4 > reader.BaseStream.Length)
                return data;

            data.value = reader.ReadUInt32();

            try
            {
                data.primaryObjectId = reader.ReadStruct<CObjectId>();
            }
            catch
            {
                return data;
            }

            if (reader.BaseStream.Position + 4 > reader.BaseStream.Length)
                return data;

            uint size = reader.ReadUInt32();
            if (size > (reader.BaseStream.Length - reader.BaseStream.Position))
                return data;

            data.skippedData = size > 0
                ? reader.ReadBytes((int)size)
                : Array.Empty<byte>();

            try
            {
                if (reader.BaseStream.Position < reader.BaseStream.Length)
                    data.trailingObjectId = reader.ReadStruct<CObjectId>();
            }
            catch
            {
            }

            return data;
        }
    }
}
