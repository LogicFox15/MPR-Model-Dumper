using AvaloniaToolbox.Core.IO;
using MetroidPrimeRemasterModelDumper.FileData;
using DKCTF;
using System.Numerics;
using static DKCTF.ROOM;

namespace MetroidPrimeRemasterModelDumper.ScriptTypes
{
    public class BakedLightingPriorityModifierMP1
    {
        public List<SMPRBakedLightingPriorityProperty> properties = new List<SMPRBakedLightingPriorityProperty>();

        // For debugging and ease of access
        public CGameObjectComponent originalComponent;
        public ScriptDataEntity originalEntity;
        public SGOComponentInstanceData originalInstanceData;

        public static BakedLightingPriorityModifierMP1 Build(CGameObjectComponent component, ScriptDataEntity entity, SGOComponentInstanceData instanceData)
        {
            BakedLightingPriorityModifierMP1 script = new BakedLightingPriorityModifierMP1();

            using (MemoryStream ms = new MemoryStream(entity.propertyData))
            using (FileReader br = new FileReader(ms))
            {
                ushort count = br.ReadUInt16();
                script.properties.Add(SMPRBakedLightingPriorityProperty.Read(br));

            }

            script.originalComponent = component;
            script.originalEntity = entity;
            script.originalInstanceData = instanceData;
            return script;
        }
    }

    public class SMPRBakedLightingPriorityProperty
    {
        public uint propertyId;
        public ushort propertySize;
        public uint priority;

        public static SMPRBakedLightingPriorityProperty Read(FileReader reader)
        {
            SMPRBakedLightingPriorityProperty prop = new SMPRBakedLightingPriorityProperty();
            prop.propertyId = reader.ReadUInt32();
            prop.propertySize = reader.ReadUInt16();
            if (prop.propertyId == 0x8582d268)
            {
                prop.priority = reader.ReadUInt32();
            }
            else if (prop.propertySize > 0)
            {
                reader.ReadBytes(prop.propertySize);
            }

            return prop;
        }
    }
}
