using AvaloniaToolbox.Core.IO;
using MetroidPrimeRemasterModelDumper.FileData;
using DKCTF;
using System.Numerics;
using static DKCTF.ROOM;

namespace MetroidPrimeRemasterModelDumper.ScriptTypes
{
    public class LightStatic
    {
        public SMPRRoomLightProperties lightProperties;

        // For debugging and ease of access
        public CGameObjectComponent originalComponent;
        public ScriptDataEntity originalEntity;
        public SGOComponentInstanceData originalInstanceData;

        public static LightStatic Build(CGameObjectComponent component, ScriptDataEntity entity, SGOComponentInstanceData instanceData)
        {
            LightStatic script = new LightStatic();

            using (MemoryStream ms = new MemoryStream(entity.propertyData))
            using (FileReader br = new FileReader(ms))
            {
                script.lightProperties = SMPRRoomLightProperties.Read(br);
            }

            script.originalComponent = component;
            script.originalEntity = entity;
            script.originalInstanceData = instanceData;
            return script;
        }
    }

    public class SMPRRoomLightProperties()
    {
        public List<SMPRRoomLightProperty> properties = new List<SMPRRoomLightProperty>();

        public static SMPRRoomLightProperties Read(FileReader reader)
        {
            SMPRRoomLightProperties prop = new SMPRRoomLightProperties();
            ushort count = reader.ReadUInt16();
            for(int i =  0; i < count; i++)
            {
                prop.properties.Add(SMPRRoomLightProperty.Read(reader));
            }

            return prop;
        }
    }

    public class SMPRRoomLightProperty
    {
        public uint propertyId;
        public ushort propertySize;

        public SMPRRoomLightProperties nested;
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

        public static SMPRRoomLightProperty Read(FileReader reader)
        {
            SMPRRoomLightProperty prop = new SMPRRoomLightProperty();
            prop.propertyId = reader.ReadUInt32();
            prop.propertySize = reader.ReadUInt16();
            switch (prop.propertyId)
            {
                case 0x8b76d48e: // SLdrLightColors
                case 0xdd21d666: // SLdrLumaIntensity
                case 0xebae52b2: // SLdrLightDistanceAttenuation
                case 0x664b1a7c: // SLdrLightAngleAttenuation
                case 0xf939c307: // SLdrLightFlags
                    prop.nested = SMPRRoomLightProperties.Read(reader);
                    break;
                case 0x52d4c579:
                    prop.lightType = reader.ReadUInt32(); // 0 ambient, 1 directional, 2 point, 3 spot, 4 area, 5/6 square
                    break;
                case 0x6c1d6c28:
                    prop.runtimeEnabled = reader.ReadByte();
                    break;
                case 0xf10ea1b7:
                    prop.baked = reader.ReadByte();
                    break;
                case 0x163bbc26:
                    prop.castShadows = reader.ReadByte();
                    break;
                case 0x7c3cd2ce:
                    prop.renderTargetScene = reader.ReadUInt32();
                    break;
                case 0x8f421907:
                    prop.color = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    break;
                case 0xdaf8bddf:
                    prop.lumaIntensity = reader.ReadSingle();
                    break;
                case 0x174c8abc:
                    prop.distanceMode = reader.ReadUInt32();
                    break;
                case 0xe5ceaf7c:
                    prop.innerRadius = reader.ReadSingle();
                    break;
                case 0x68ac20b3:
                    prop.outerRadius = reader.ReadSingle();
                    break;
                case 0x635dfcc7:
                    prop.innerAngleDegrees = reader.ReadSingle();
                    break;
                case 0xb0a71764:
                    prop.outerAngleDegrees = reader.ReadSingle();
                    break;
                case 0xa103d675:
                    prop.lightFlag0 = reader.ReadByte();
                    break;
                case 0x56608a5c:
                    prop.lightFlag1 = reader.ReadByte();
                    break;
                case 0xeeb94af2:
                    prop.lightFlag2 = reader.ReadByte();
                    break;
                case 0x67241f0c:
                    prop.lightFlag3 = reader.ReadByte();
                    break;
                case 0x3f1e6850:
                    prop.fillAmbient = reader.ReadByte();
                    break;
                default:
                    if (prop.propertySize > 0)
                    {
                        reader.ReadBytes(prop.propertySize);
                    }
                    break;
            }

            return prop;
        }
    }
}
