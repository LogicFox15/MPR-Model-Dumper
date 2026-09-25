using AvaloniaToolbox.Core.IO;
using MetroidPrimeRemasterModelDumper.FileData;
using DKCTF;
using System.Numerics;
using static DKCTF.ROOM;

namespace MetroidPrimeRemasterModelDumper.ScriptTypes
{
    public class LightStatic
    {
        public CommonObjectData commonObjectData = new CommonObjectData();

        public uint lightType;
        public byte runtimeEnabled;
        public byte baked;
        public byte castShadows;            // Unconfirmed, but would make sense
        public uint renderTargetScene;      // Use Hex
        public Vector4 color;
        public float lumaIntensity;
        public uint distanceMode;           // Use Hex
        public float innerRadius;
        public float outerRadius;
        public float innerAngleDegrees = 0;     // Full Cone Radius
        public float outerAngleDegrees = 45;     // Full Cone Radius
        public byte lightFlag0;
        public byte lightFlag1;
        public byte lightFlag2;
        public byte lightFlag3;
        public byte fillAmbient;

        public static LightStatic Build(CGameObjectComponent component, ScriptDataEntity entity, SGOComponentInstanceData instanceData)
        {
            LightStatic script = new LightStatic();

            using (MemoryStream ms = new MemoryStream(entity.propertyData))
            using (FileReader br = new FileReader(ms))
            {
                BuildRoomLightProperties(br, script);
            }

            script.commonObjectData.originalComponent = component;
            script.commonObjectData.originalEntity = entity;
            script.commonObjectData.originalInstanceData = instanceData;
            return script;
        }

        public static LightStatic prepTransform(LightStatic retroObject, ConstructedLayer parsed)
        {
            foreach (var prop in parsed.entityProperties)
            {
                foreach (var link in prop.originalInstanceData.links)
                {
                    if (link.target.ToString() == retroObject.commonObjectData.originalInstanceData.id.ToString())
                    {
                        retroObject.commonObjectData.entityProperties = prop;
                        break;
                    }
                }
            }
            return retroObject;
        }

        public static void BuildRoomLightProperties(FileReader reader, LightStatic script)
        {
            ushort count = reader.ReadUInt16();

            for (int i = 0; i < count; i++)
            {
                ReadRoomLightProperties(reader, script);
            }
        }

        public static void ReadRoomLightProperties(FileReader reader, LightStatic script)
        {
            uint propertyId = reader.ReadUInt32();
            ushort propertySize = reader.ReadUInt16();

            byte[] propertyData = propertySize > 0
                ? reader.ReadBytes(propertySize)
                : Array.Empty<byte>();

            using MemoryStream ms = new MemoryStream(propertyData);
            using FileReader propertyReader = new FileReader(ms);

            switch (propertyId)
            {
                // Nested light property groups
                case 0x8b76d48e: // SLdrLightColors
                case 0xdd21d666: // SLdrLumaIntensity
                case 0xebae52b2: // SLdrLightDistanceAttenuation
                case 0x664b1a7c: // SLdrLightAngleAttenuation
                case 0xf939c307: // SLdrLightFlags
                    BuildRoomLightProperties(propertyReader, script);
                    break;
                case 0x52d4c579: // Light type
                    script.lightType = propertyReader.ReadUInt32();
                    break;
                case 0x6c1d6c28: // Runtime enabled
                    script.runtimeEnabled = propertyReader.ReadByte();
                    break;
                case 0xf10ea1b7: // Baked
                    script.baked = propertyReader.ReadByte();
                    break;
                case 0x163bbc26: // Cast shadows / unknown flag
                    script.castShadows = propertyReader.ReadByte();
                    break;
                case 0x7c3cd2ce: // Render target scene
                    script.renderTargetScene = propertyReader.ReadUInt32();
                    break;
                case 0x8f421907: // Color
                    script.color = new Vector4(
                        propertyReader.ReadSingle(),
                        propertyReader.ReadSingle(),
                        propertyReader.ReadSingle(),
                        propertyReader.ReadSingle());
                    break;
                case 0xdaf8bddf: // Luma intensity
                    script.lumaIntensity = propertyReader.ReadSingle();
                    break;
                case 0x174c8abc: // Distance mode
                    script.distanceMode = propertyReader.ReadUInt32();
                    break;
                case 0xe5ceaf7c: // Inner radius
                    script.innerRadius = propertyReader.ReadSingle();
                    break;
                case 0x68ac20b3: // Outer radius
                    script.outerRadius = propertyReader.ReadSingle();
                    break;
                case 0x635dfcc7: // Inner angle
                    script.innerAngleDegrees = propertyReader.ReadSingle();
                    break;
                case 0xb0a71764: // Outer angle
                    script.outerAngleDegrees = propertyReader.ReadSingle();
                    break;
                case 0xa103d675:
                    script.lightFlag0 = propertyReader.ReadByte();
                    break;
                case 0x56608a5c:
                    script.lightFlag1 = propertyReader.ReadByte();
                    break;
                case 0xeeb94af2:
                    script.lightFlag2 = propertyReader.ReadByte();
                    break;
                case 0x67241f0c:
                    script.lightFlag3 = propertyReader.ReadByte();
                    break;
                case 0x3f1e6850:
                    script.fillAmbient = propertyReader.ReadByte();
                    break;
                default:
                    // Unknown properties are already consumed because we copied
                    // propertySize bytes into propertyData.
                    break;
            }
        }
    }
}
