using AvaloniaToolbox.Core.IO;
using MetroidPrimeRemasterModelDumper.FileData;
using DKCTF;
using System.Numerics;
using static DKCTF.ROOM;

namespace MetroidPrimeRemasterModelDumper.ScriptTypes
{
    public class ColorModifier
    {
        public SMPRColorModifierProperties colorModifierProperties;

        // For debugging and ease of access
        public CGameObjectComponent originalComponent;
        public ScriptDataEntity originalEntity;
        public SGOComponentInstanceData originalInstanceData;

        public static ColorModifier Build(CGameObjectComponent component, ScriptDataEntity entity, SGOComponentInstanceData instanceData)
        {
            ColorModifier script = new ColorModifier();

            using (MemoryStream ms = new MemoryStream(entity.propertyData))
            using (FileReader br = new FileReader(ms))
            {
                script.colorModifierProperties = SMPRColorModifierProperties.Read(br);
            }

            script.originalComponent = component;
            script.originalEntity = entity;
            script.originalInstanceData = instanceData;
            return script;
        }
    }

    public class SMPRColorModifierProperties()
    {
        public List<SMPRColorModifierProperty> properties = new List<SMPRColorModifierProperty>();

        public static SMPRColorModifierProperties Read(FileReader reader)
        {
            SMPRColorModifierProperties prop = new SMPRColorModifierProperties();
            ushort count = reader.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                prop.properties.Add(SMPRColorModifierProperty.Read(reader));
            }

            return prop;
        }
    }

    public class SMPRColorModifierProperty
    {
        public uint propertyId;
        public ushort propertySize;

        public Vector4 initialColor;
        public Vector4 endColor;
        public CMPRColorGradient gradient;
        public CMayaSpline timing;
        public SMPRColorModifierOptions options;
        public EMPRColorModifierMode mode;
        public float initialIntensity;
        public float endIntensity;

        public static SMPRColorModifierProperty Read(FileReader reader)
        {
            SMPRColorModifierProperty prop = new SMPRColorModifierProperty();
            prop.propertyId = reader.ReadUInt32();
            prop.propertySize = reader.ReadUInt16();
            switch (prop.propertyId)
            {
                case 0x8133a24b:
                    prop.initialColor = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    break;
                case 0xd968bd8e:
                    prop.endColor = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    break;
                case 0xdbe10078:
                    prop.gradient = CMPRColorGradient.Read(reader);
                    break;
                case 0x34d88fef:
                    prop.timing = reader.ReadStruct<CMayaSpline>();
                    break;
                case 0xb7bf28fd:
                    prop.options = SMPRColorModifierOptions.Read(reader);
                    break;
                case 0xa3fbf12b:
                    prop.mode = (EMPRColorModifierMode)reader.ReadUInt32();
                    break;
                case 0x627d5359:
                    prop.initialIntensity = reader.ReadSingle();
                    break;
                case 0xc1fd4866:
                    prop.endIntensity = reader.ReadSingle();
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

    public class SMPRColorModifierOptions()
    {
        public List<SMPRColorModifierOption> options = new List<SMPRColorModifierOption>();

        public static SMPRColorModifierOptions Read(FileReader reader)
        {
            SMPRColorModifierOptions prop = new SMPRColorModifierOptions();
            ushort count = reader.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                prop.options.Add(SMPRColorModifierOption.Read(reader));
            }

            return prop;
        }
    }

    public class SMPRColorModifierOption
    {
        public uint propertyId;
        public ushort propertySize;

        public float initialTime;
        public float randomTimeRange;
        public byte initiallyPlaying;
        public byte applyFromStart;
        public byte applyGradientFromStart;
        public byte applyEndColor;
        public byte applyGradientEnd;
        public byte applyInitialTime;
        public byte looping;
        public byte useGradient;
        public byte useThinkDelta;

        public static SMPRColorModifierOption Read(FileReader reader)
        {
            SMPRColorModifierOption prop = new SMPRColorModifierOption();
            prop.propertyId = reader.ReadUInt32();
            prop.propertySize = reader.ReadUInt16();
            switch (prop.propertyId)
            {
                case 0x28376d3f:
                    prop.initialTime = reader.ReadSingle();
                    break;
                case 0xb1437b20:
                    prop.randomTimeRange = reader.ReadSingle();
                    break;
                case 0xe4c9cd3e:
                    prop.initiallyPlaying = reader.ReadByte();
                    break;
                case 0x47a9b8f4:
                    prop.applyFromStart = reader.ReadByte();
                    break;
                case 0xcb60e6ae:
                    prop.applyGradientFromStart = reader.ReadByte();
                    break;
                case 0xe000181c:
                    prop.applyEndColor = reader.ReadByte();
                    break;
                case 0xf9159caf:
                    prop.applyGradientEnd = reader.ReadByte();
                    break;
                case 0xff17f0bb:
                    prop.applyInitialTime = reader.ReadByte();
                    break;
                case 0xcb494abc:
                    prop.looping = reader.ReadByte();
                    break;
                case 0x68b9c38e:
                    prop.useGradient = reader.ReadByte();
                    break;
                case 0x83737173:
                    prop.useThinkDelta = reader.ReadByte();
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

    public class CMPRColorGradient()
    {
        public List<CMayaSpline> channels = new List<CMayaSpline>();

        public static CMPRColorGradient Read(FileReader reader)
        {
            CMPRColorGradient grad = new CMPRColorGradient();
            ushort count = reader.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                grad.channels.Add(reader.ReadStruct<CMayaSpline>());
            }

            return grad;
        }
    }

    public enum EMPRColorModifierMode : uint
    {
        MPRColorModifierGeneral = 0x6e1abcf4,
        MPRColorModifierBakedLighting = 0xd2fd45bc,
        MPRColorModifierColorEffect0 = 0x209ea819,
        MPRColorModifierColorEffect1 = 0x586d50a4,
    }

}
