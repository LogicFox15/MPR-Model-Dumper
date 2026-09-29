using AvaloniaToolbox.Core.IO;
using MetroidPrimeRemasterModelDumper.FileData;
using DKCTF;
using System.Numerics;
using static DKCTF.ROOM;

namespace MetroidPrimeRemasterModelDumper.ScriptTypes
{
    public class ModConScript
    {
        public CommonObjectData commonObjectData = new CommonObjectData();

        public CObjectId modularConstructionId;
        public CDataEnumBitField bitField;
        public uint renderTargetScene;
        public byte unknownFlag;

        public static ModConScript Build(CGameObjectComponent component, ScriptDataEntity entity, SGOComponentInstanceData instanceData)
        {
            ModConScript script = new ModConScript();

            using (MemoryStream ms = new MemoryStream(entity.propertyData))
            using (FileReader br = new FileReader(ms))
            {
                BuildModConProperties(br, script);
            }

            script.commonObjectData.originalComponent = component;
            script.commonObjectData.originalEntity = entity;
            script.commonObjectData.originalInstanceData = instanceData;
            return script;
        }

        public static ModConScript prepTransform(ModConScript retroObject, ConstructedLayer parsed)
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

        public static void BuildModConProperties(FileReader reader, ModConScript script)
        {
            ushort count = reader.ReadUInt16();
            for (int i = 0; i < count; i++)
            {
                ReadModConProperties(reader, script);
            }
        }

        public static void ReadModConProperties(FileReader reader, ModConScript script)
        {
            var propertyId = reader.ReadUInt32();
            var propertySize = reader.ReadUInt16();

            byte[] propertyData = propertySize > 0
                ? reader.ReadBytes(propertySize)
                : Array.Empty<byte>();

            using MemoryStream ms = new MemoryStream(propertyData);
            using FileReader propertyReader = new FileReader(ms);

            switch (propertyId)
            {
                // SLdrModCon
                case 0xA8E2BA93:
                    script.modularConstructionId = propertyReader.ReadStruct<CObjectId>();
                    break;
                case 0xF068D36B:
                    script.bitField = CDataEnumBitField.Read(propertyReader);
                    break;
                case 0x356DB82B:
                    script.renderTargetScene = propertyReader.ReadUInt32();
                    break;
                case 0x61BE7D93:
                    script.unknownFlag = propertyReader.ReadByte();
                    break;
                default:
                    break;
            }
        }
    }
}
