using AvaloniaToolbox.Core.IO;
using DKCTF;
using MetroidPrimeRemasterModelDumper;
using MetroidPrimeRemasterModelDumper.ScriptTypes;
using RetroStudioPlugin.Files.FileData;
using RoomParser;
using System;
using System.Numerics;
using static DKCTF.ROOM;
#nullable disable

public class WaterRenderVolume
{
    public CommonObjectData commonObjectData = new CommonObjectData();

    // Top-Level WaterRenderVolume references
    public CObjectId waterModel;
    public CObjectId runtimeId;

    // Water material references
    public CObjectId waterNormalMap;
    public CObjectId projectedLightmap;
    public CObjectId rainDropRippleObject;

    // Water material Color
    public Color4f waterColor;

    // Water Fog
    public Color4f waterFogColor;
    public float waterFogUnknown;

    // Normal-map parameters
    public uint waterDirectionA;
    public uint waterDirectionB;
    public float normalMapFloatA;
    public float normalMapFloatB;

    // Feature flags
    public bool[] featureFlags = new bool[11];

    // Wave parameters
    public float[] waveParamsA = new float[5];
    public float[] waveParamsB = new float[5];

    // Additional material values
    public float materialFloat0;
    public float materialFloat1;
    public float materialFloat2;
    public float materialFloat3;
    public float materialFloat4;

    // Rain/ripple parameters
    public float[] rainDropRippleParams = new float[9];

    public static WaterRenderVolume Build(CGameObjectComponent component, ScriptDataEntity entity, SGOComponentInstanceData instanceData)
    {
        WaterRenderVolume script = new WaterRenderVolume();

        using (MemoryStream ms = new MemoryStream(entity.propertyData))
        using (FileReader br = new FileReader(ms))
        {
            BuildWaterRenderVolumeProperties(br, script);
        }

        script.commonObjectData.originalComponent = component;
        script.commonObjectData.originalEntity = entity;
        script.commonObjectData.originalInstanceData = instanceData;
        return script;
    }

    public static WaterRenderVolume prepTransform(WaterRenderVolume retroObject, ConstructedLayer parsed)
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

    public static void BuildWaterRenderVolumeProperties(FileReader reader, WaterRenderVolume script)
    {
        ushort count = reader.ReadUInt16();
        for (int i = 0; i < count; i++)
        {
            ReadWaterRenderVolumeProperties(reader, script);
        }
    }

    public static void ReadWaterRenderVolumeProperties(FileReader reader, WaterRenderVolume script)
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
            // ============================================================
            // Top-level WaterRenderVolume
            // ============================================================
            case 0x736e5890: // Water model
                script.waterModel = propertyReader.ReadStruct<CObjectId>();
                break;
            case 0x7470b292: // Runtime Id
                script.runtimeId = propertyReader.ReadStruct<CObjectId>();
                break;
            case 0x54f39685: // Water feature flags
                ReadWaterFeatureFlags(propertyReader, script);
                break;
            case 0x30fbb790: // Wave parameter set A
                ReadWaterWaveParams(propertyReader, script.waveParamsA);
                break;
            case 0x0951bf3e: // Wave parameter set B
                ReadWaterWaveParams(propertyReader, script.waveParamsB);
                break;
            case 0xd1e9d29d: // Water material data
                BuildWaterRenderVolumeProperties(propertyReader, script);
                break;
            // ============================================================
            // Water feature flags
            // ============================================================
            case 0xbf2c11db:
                script.featureFlags[3] = propertyReader.ReadByte() != 0;
                break;
            case 0x54ad680d:
                script.featureFlags[6] = propertyReader.ReadByte() != 0;
                break;
            case 0x1878894a:
                script.featureFlags[7] = propertyReader.ReadByte() != 0;
                break;
            case 0x8c79533e:
                script.featureFlags[9] = propertyReader.ReadByte() != 0;
                break;
            case 0xd7f0419b:
                script.featureFlags[2] = propertyReader.ReadByte() != 0;
                break;
            case 0xf0965f0d:
                script.featureFlags[8] = propertyReader.ReadByte() != 0;
                break;
            case 0xf2d5d4be:
                script.featureFlags[4] = propertyReader.ReadByte() != 0;
                break;
            case 0xfcfce6e6:
                script.featureFlags[5] = propertyReader.ReadByte() != 0;
                break;
            case 0x68999d0e:
                script.featureFlags[1] = propertyReader.ReadByte() != 0;
                break;
            case 0x8b294d2e:
                script.featureFlags[0] = propertyReader.ReadByte() != 0;
                break;
            case 0x802d7816:
                script.featureFlags[10] = propertyReader.ReadByte() != 0;
                break;
            // ============================================================
            // WaterNormalMap
            // ============================================================
            case 0x03e33f4b: // Normal map container
                BuildWaterRenderVolumeProperties(propertyReader, script);
                break;
            case 0x90a143ef: // Normal map object
                script.waterNormalMap = propertyReader.ReadStruct<CObjectId>();
                break;
            case 0x138db2f0: // Water direction
                BuildWaterRenderVolumeProperties(propertyReader, script);
                break;
            case 0xb9303984:
                script.waterDirectionA = propertyReader.ReadUInt32();
                break;
            case 0x51f42492:
                script.waterDirectionB = propertyReader.ReadUInt32();
                break;
            case 0x522abd8a:
                script.normalMapFloatA = propertyReader.ReadSingle();
                break;
            case 0xd4483c7e:
                script.normalMapFloatB = propertyReader.ReadSingle();
                break;
            // ============================================================
            // WaterFog
            // ============================================================
            case 0x7982e07b: // Water fog container
                BuildWaterRenderVolumeProperties(propertyReader, script);
                break;
            case 0x6f7355a3: // Fog color
                script.waterFogColor = propertyReader.ReadStruct<Color4f>();
                break;
            case 0x3b29e512:
                script.waterFogUnknown = propertyReader.ReadSingle();
                break;
            // ============================================================
            // Projected lightmap
            // ============================================================
            case 0xdcee7d4d:
                BuildWaterRenderVolumeProperties(propertyReader, script);
                break;
            case 0x797a7ff1:
                script.projectedLightmap = propertyReader.ReadStruct<CObjectId>();
                break;
            // ============================================================
            // Rain drop ripple data
            // ============================================================
            case 0x1a6aac69:
                BuildWaterRenderVolumeProperties(propertyReader, script);
                break;
            case 0xa61e08d3:
                script.rainDropRippleObject = propertyReader.ReadStruct<CObjectId>();
                break;
            case 0xe4732770:
                script.rainDropRippleParams[0] = propertyReader.ReadSingle();
                break;
            case 0x5e94c719:
                script.rainDropRippleParams[1] = propertyReader.ReadSingle();
                break;
            case 0xe89f4385:
                script.rainDropRippleParams[2] = propertyReader.ReadSingle();
                break;
            case 0xc5dad5ec:
                script.rainDropRippleParams[3] = propertyReader.ReadSingle();
                break;
            case 0xbc212b78:
                script.rainDropRippleParams[4] = propertyReader.ReadSingle();
                break;
            case 0x7b56b6dd:
                script.rainDropRippleParams[5] = propertyReader.ReadSingle();
                break;
            case 0x3bda1181:
                script.rainDropRippleParams[6] = propertyReader.ReadSingle();
                break;
            case 0x6e5a8b34:
                script.rainDropRippleParams[7] = propertyReader.ReadSingle();
                break;
            case 0x851d5509:
                script.rainDropRippleParams[8] = propertyReader.ReadSingle();
                break;
            // ============================================================
            // Water material data
            // ============================================================
            case 0xe8969fad:
                script.waterColor = propertyReader.ReadStruct<Color4f>();
                break;
            case 0x9201a855:
                script.materialFloat0 = propertyReader.ReadSingle();
                break;
            case 0x00c267c0:
                script.materialFloat1 = propertyReader.ReadSingle();
                break;
            case 0x50c8ac86:
                script.materialFloat2 = propertyReader.ReadSingle();
                break;
            case 0xd8afdce6:
                script.materialFloat3 = propertyReader.ReadSingle();
                break;
            case 0x513d8344:
                script.materialFloat4 = propertyReader.ReadSingle();
                break;
            default:
                break;
        }
    }

    private static void ReadWaterFeatureFlags(FileReader reader, WaterRenderVolume script)
    {
        ushort count = reader.ReadUInt16();
        for (int i = 0; i < count; i++)
        {
            ReadWaterRenderVolumeProperties(reader, script);
        }
    }

    private static void ReadWaterWaveParams(FileReader reader, float[] destination)
    {
        ushort count = reader.ReadUInt16();

        for (int i = 0; i < count; i++)
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
                case 0xd1edd7b5:
                    destination[0] = propertyReader.ReadSingle();
                    break;
                case 0xabe1bacd:
                    destination[1] = propertyReader.ReadSingle();
                    break;
                case 0xf6266364:
                    destination[2] = propertyReader.ReadSingle();
                    break;
                case 0x4574de4f:
                    destination[3] = propertyReader.ReadSingle();
                    break;
                case 0x921415eb:
                    destination[4] = propertyReader.ReadSingle();
                    break;
                default:
                    break;
            }
        }
    }




}
