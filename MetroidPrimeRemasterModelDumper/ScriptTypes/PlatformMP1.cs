using AvaloniaToolbox.Core.IO;
using DKCTF;
using MetroidPrimeRemasterModelDumper;
using MetroidPrimeRemasterModelDumper.ScriptTypes;
using RetroStudioPlugin.Files.FileData;
using RoomParser;
using System;
using System.Numerics;
using static DKCTF.ROOM;

public class PlatformMP1
{
    public CommonObjectData commonObjectData = new CommonObjectData();

    public CObjectId staticModelId;

    public static PlatformMP1 Build(CGameObjectComponent component, ScriptDataEntity entity, SGOComponentInstanceData instanceData)
    {
        PlatformMP1 script = new PlatformMP1();

        using (MemoryStream ms = new MemoryStream(entity.propertyData))
        using (FileReader br = new FileReader(ms))
        {
            BuildPlatformMP1Properties(br, script);
        }

        script.commonObjectData.originalComponent = component;
        script.commonObjectData.originalEntity = entity;
        script.commonObjectData.originalInstanceData = instanceData;
        return script;
    }

    public static PlatformMP1 prepTransform(PlatformMP1 retroObject, ConstructedLayer parsed)
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

    public static void BuildPlatformMP1Properties(FileReader reader, PlatformMP1 script)
    {
        ushort count = reader.ReadUInt16();
        for (int i = 0; i < count; i++)
        {
            ReadPlatformMP1Properties(reader, script);
        }
    }

    public static void ReadPlatformMP1Properties(FileReader reader, PlatformMP1 script)
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
            case 0xf8b4c8da:    // Generic Model
                script.staticModelId = propertyReader.ReadStruct<CObjectId>();
                break;
            default:
                break;
        }
    }
}
