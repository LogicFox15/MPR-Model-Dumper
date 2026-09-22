using AvaloniaToolbox.Core.IO;
using MetroidPrimeRemasterModelDumper.FileData;
using DKCTF;
using System.Numerics;
using static DKCTF.ROOM;

namespace MetroidPrimeRemasterModelDumper.ScriptTypes
{
    public class ModConScript
    {
        public SMPRModConProperties modConProperties;

        // For debugging and ease of access
        public CGameObjectComponent originalComponent;
        public ScriptDataEntity originalEntity;
        public SGOComponentInstanceData originalInstanceData;

        public static ModConScript Build(CGameObjectComponent component, ScriptDataEntity entity, SGOComponentInstanceData instanceData)
        {
            ModConScript script = new ModConScript();

            using (MemoryStream ms = new MemoryStream(entity.propertyData))
            using (FileReader br = new FileReader(ms))
            {
                script.modConProperties = SMPRModConProperties.Read(br);
            }

            script.originalComponent = component;
            script.originalEntity = entity;
            script.originalInstanceData = instanceData;
            return script;
        }
    }

    public class SMPRModConProperties()
    {
        public List<SMPRModConProperty> properties = new List<SMPRModConProperty>();

        public static SMPRModConProperties Read(FileReader reader)
        {
            SMPRModConProperties prop = new SMPRModConProperties();
            ushort count = reader.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                prop.properties.Add(SMPRModConProperty.Read(reader));
            }

            return prop;
        }
    }

    public class SMPRModConProperty
    {
        public uint propertyId;
        public ushort propertySize;

        public CObjectId modularConstructionId;
        public CDataEnumBitField bitField;
        public uint renderTargetScene;
        public byte unknownFlag;

        public static SMPRModConProperty Read(FileReader reader)
        {
            SMPRModConProperty prop = new SMPRModConProperty();
            prop.propertyId = reader.ReadUInt32();
            prop.propertySize = reader.ReadUInt16();
            switch (prop.propertyId)
            {
                // SLdrModCon
                case 0xA8E2BA93:
                    prop.modularConstructionId = reader.ReadStruct<CObjectId>();
                    break;
                case 0xF068D36B:
                    prop.bitField = CDataEnumBitField.Read(reader);
                    break;
                case 0x356DB82B:
                    prop.renderTargetScene = reader.ReadUInt32();
                    break;
                case 0x61BE7D93:
                    prop.unknownFlag = reader.ReadByte();
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
