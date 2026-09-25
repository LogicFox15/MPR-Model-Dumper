using AvaloniaToolbox.Core.IO;
using DKCTF;
using MetroidPrimeRemasterModelDumper;
using MetroidPrimeRemasterModelDumper.ScriptTypes;
using RetroStudioPlugin.Files.FileData;
using RoomParser;
using System;
using System.Numerics;
using static DKCTF.ROOM;

public class LavaRenderVolume
{
    public CommonObjectData commonObjectData = new CommonObjectData();

    public CObjectId lavaModelId;
    public float lavaVectorX;
    public float lavaVectorY;
    public float lavaVectorZ;
    public float lavaVectorW;
    public float unk1;
    public float unk2;
    public float unk3;
    public float unk4;
    public float optUnk1;
    public float optUnk2;

    public static LavaRenderVolume Build(CGameObjectComponent component, ScriptDataEntity entity, SGOComponentInstanceData instanceData)
    {
        LavaRenderVolume script = new LavaRenderVolume();

        using (MemoryStream ms = new MemoryStream(entity.propertyData))
        using (FileReader reader = new FileReader(ms))
        {
            BuildLavaRenderVolumeProperties(reader, script);
        }

        script.commonObjectData.originalComponent = component;
        script.commonObjectData.originalEntity = entity;
        script.commonObjectData.originalInstanceData = instanceData;
        return script;
    }

    public static LavaRenderVolume prepTransform(LavaRenderVolume retroObject, ConstructedLayer parsed)
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

    public static void BuildLavaRenderVolumeProperties(FileReader reader, LavaRenderVolume script)
    {
        ushort count = reader.ReadUInt16();
        for (int i = 0; i < count; i++)
        {
            ReadLavaRenderVolumeProperties(reader, script);
        }
    }

    public static void ReadLavaRenderVolumeProperties(FileReader reader, LavaRenderVolume script)
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
            case 0xcaf8e8c3:    // Lava Model
                script.lavaModelId = propertyReader.ReadStruct<CObjectId>();
                break;
            case 0xeee67cd7:    // Nested Lava Vector
                BuildLavaRenderVolumeProperties(propertyReader, script);
                break;
            case 0x54c1ad83:    // Lava Vector X
                script.lavaVectorX = propertyReader.ReadSingle();
                break;
            case 0x8e9d06a4:    // Lava Vector Y
                script.lavaVectorY = propertyReader.ReadSingle();
                break;
            case 0x851b5ca5:    // Lava Vector Z
                script.lavaVectorZ = propertyReader.ReadSingle();
                break;
            case 0xf8bed071:    // Lava Vector W
                script.lavaVectorW = propertyReader.ReadSingle();
                break;
            case 0x82330004:    // Unk 1
                script.unk1 = propertyReader.ReadSingle();
                break;
            case 0x4b27a58d:    // Unk 2
                script.unk2 = propertyReader.ReadSingle();
                break;
            case 0x30a47f17:    // Unk 3
                script.unk3 = propertyReader.ReadSingle();
                break;
            case 0x4ec7028f:    // Unk 4
                script.unk4 = propertyReader.ReadSingle();
                break;
            case 0x36062e0f:    // Optional Unk 1
                script.optUnk1 = propertyReader.ReadSingle();
                break;
            case 0xe7993ff7:    // Optional Unk 2
                script.optUnk1 = propertyReader.ReadSingle();
                break;
            default:
                break;
        }
    }
}
