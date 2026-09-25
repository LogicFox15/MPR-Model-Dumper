using AvaloniaToolbox.Core.IO;
using DKCTF;
using MetroidPrimeRemasterModelDumper;
using MetroidPrimeRemasterModelDumper.ScriptTypes;
using RetroStudioPlugin.Files.FileData;
using RoomParser;
using System;
using System.Numerics;
using static DKCTF.ROOM;

public class ActorMP1
{
    public CommonObjectData commonObjectData = new CommonObjectData();

    public CObjectId staticModelId;
    public CObjectId chprId;

    public static ActorMP1 Build(CGameObjectComponent component, ScriptDataEntity entity, SGOComponentInstanceData instanceData)
    {
        ActorMP1 script = new ActorMP1();

        using (MemoryStream ms = new MemoryStream(entity.propertyData))
        using (FileReader br = new FileReader(ms))
        {
            BuildActorMP1Properties(br, script);
        }

        script.commonObjectData.originalComponent = component;
        script.commonObjectData.originalEntity = entity;
        script.commonObjectData.originalInstanceData = instanceData;
        return script;
    }

    public static ActorMP1 prepTransform(ActorMP1 retroObject, ConstructedLayer parsed)
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

    public static void BuildActorMP1Properties(FileReader reader, ActorMP1 script)
    {
        ushort count = reader.ReadUInt16();
        for (int i = 0; i < count; i++)
        {
            ReadActorMP1Properties(reader, script);
        }
    }

    public static void ReadActorMP1Properties(FileReader reader, ActorMP1 script)
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
            case 0x54446d42:    // Nested CHPR container
                BuildActorMP1Properties(propertyReader, script);
                break;
            case 0xcb1c52f6:    // Generic Model
                script.staticModelId = propertyReader.ReadStruct<CObjectId>();
                break;
            case 0xa589d885:    // Generic CHPR
                script.chprId = propertyReader.ReadStruct<CObjectId>();
                break;
            default:
                break;
        }
    }
}
