using AvaloniaToolbox.Core.IO;
using DKCTF;
using IONET.Collada.Core.Data_Flow;
using MetroidPrimeRemasterModelDumper.FileData;
using MetroidPrimeRemasterModelDumper.Tools;
using System.Numerics;
using System.Runtime.CompilerServices;
#nullable disable

namespace DKCTF
{
    public class ROOM : FileForm
    {
        public RoomHeaderChunk HeadChunk = new RoomHeaderChunk();
        public StringPool StrpChunk = new StringPool();
        public ScriptDataChunk ScriptData = new ScriptDataChunk();
        public RoomLayersChunk LayersChunk = new RoomLayersChunk();

        // Meta Data
        public RoomMetaData Meta;

        public ROOM(System.IO.Stream stream) : base(stream)
        {
        }

        public override void Read(FileReader reader)
        {
            // Retain this to undo the Big Endian override hack in the FileForm constructor
            reader.SetByteOrder(false);
            HeadChunk = RoomHeaderChunk.Read(reader);
            StrpChunk = StringPool.Read(reader);
            ScriptData = ScriptDataChunk.Read(reader);
            LayersChunk = RoomLayersChunk.Read(reader);
        }

        public override void ReadMetaData(FileReader reader, CFormDescriptor pakVersion)
        {
            Meta = new RoomMetaData();
            Meta.version = reader.ReadUInt16();
            if(Meta.version == 0x19)
            {
                Meta.type = (ERoomType)reader.ReadUInt32();
                Meta.game = reader.ReadStruct<Magic>();
                Meta.parentRoomId = reader.ReadStruct<CObjectId>();
                Meta.unk1 = reader.ReadUInt16();
                Meta.unk2 = reader.ReadBytes(3);
                Meta.persistenceStateCount = reader.ReadUInt32();
                for(int i = 0; i < Meta.persistenceStateCount; i++)
                {
                    Meta.states.Add(SGameAreaPersistenceState.Read(reader));
                }
                Meta.bounds = reader.ReadStruct<CAABox>();
                Meta.loadUnitMetaCount = reader.ReadUInt32();
                for (int i = 0; i < Meta.loadUnitMetaCount; i++)
                {
                    Meta.loadUnitMetaData.Add(SScriptLoadUnitMetaData.Read(reader));
                }
                Meta.envVarCount = reader.ReadUInt16();
                for (int i = 0; i < Meta.envVarCount; i++)
                {
                    Meta.envVar.Add(SEnvironmentVar.Read(reader));
                }
                Meta.unkMetaStructCount = reader.ReadUInt32();
                for (int i = 0; i < Meta.unkMetaStructCount; i++)
                {
                    Meta.unkMetaStructs.Add(unkMetaStruct.Read(reader));
                }
                Meta.byteCount = reader.ReadUInt32();
            }
        }

        #region HEAD Chunk
        public class RoomHeaderChunk()
        {
            public CFormDescriptor formDescriptor;
            public SGameAreaHeader roomHeader;
            public CPerformanceGroupManagerResourceData performanceGroupManagerResourceData;
            public GeneratedObjectMap generatedObjectMap;
            public DOCK docks;
            public BakedLighting bakedLighting; // BLIT
            public CScriptLoadUnitResourceData loadUnits;

            public static RoomHeaderChunk Read(FileReader br)
            {
                RoomHeaderChunk head = new RoomHeaderChunk();
                head.formDescriptor = br.ReadStruct<CFormDescriptor>();
                Console.WriteLine("Room Header Position: " + br.BaseStream.Position.ToString("X8"));
                head.roomHeader = SGameAreaHeader.Read(br);
                Console.WriteLine("Performance Group Manager Position: " + br.BaseStream.Position.ToString("X8"));
                head.performanceGroupManagerResourceData = CPerformanceGroupManagerResourceData.Read(br);
                Console.WriteLine("Generated Object Map Position: " + br.BaseStream.Position.ToString("X8"));
                head.generatedObjectMap = GeneratedObjectMap.Read(br);
                Console.WriteLine("Docks Position: " + br.BaseStream.Position.ToString("X8"));
                head.docks = DOCK.Read(br);
                Console.WriteLine("Baked Lighting Position: " + br.BaseStream.Position.ToString("X8"));
                head.bakedLighting = BakedLighting.Read(br);
                Console.WriteLine("Load Units Position: " + br.BaseStream.Position.ToString("X8"));
                head.loadUnits = CScriptLoadUnitResourceData.Read(br);
                return head;
            }
        }

        public class SGameAreaHeader // RMHD
        {
            public CChunkDescriptor chunkDescriptor;
            public CObjectId parentRoomId;
            public ERoomType roomType;
            public ushort unk2;
            public byte unk3;
            public CObjectId id_b;
            public CObjectId id_c;
            public CObjectId id_d;
            public CObjectId id_e;
            public CObjectId pathFindAreaId;
            public ProductionWorkStages productionWorkStages;

            public static SGameAreaHeader Read(FileReader reader)
            {
                SGameAreaHeader header = new SGameAreaHeader();
                header.chunkDescriptor = reader.ReadStruct<CChunkDescriptor>();
                header.parentRoomId = reader.ReadStruct<CObjectId>();
                header.roomType = (ERoomType)reader.ReadUInt16();
                header.unk2 = reader.ReadUInt16();
                header.unk3 = reader.ReadByte();
                header.id_b = reader.ReadStruct<CObjectId>();
                header.id_c = reader.ReadStruct<CObjectId>();
                header.id_d = reader.ReadStruct<CObjectId>();
                header.id_e = reader.ReadStruct<CObjectId>();
                header.pathFindAreaId = reader.ReadStruct<CObjectId>();
                header.productionWorkStages = ProductionWorkStages.Read(reader);
                return header;
            }
        }

        public class ProductionWorkStages
        {
            public ushort count;
            public List<WorkStage> workStages;

            public static ProductionWorkStages Read(FileReader br)
            {
                ProductionWorkStages stages = new ProductionWorkStages();
                stages.count = br.ReadUInt16();
                //Console.WriteLine("Work Stage Count: " + stages.count.ToString());
                stages.workStages = new List<WorkStage>();
                for (int i = 0; i < stages.count; i++)
                {
                    stages.workStages.Add(WorkStage.Read(br));
                }

                return stages;
            }
        }

        public class WorkStage
        {
            public uint id;
            public ushort size;
            public uint productionStage;
            public CDataEnumValue val;

            public static WorkStage Read(FileReader reader)
            {
                //Console.WriteLine("Work Stage Reader Position: " + br.BaseStream.Position.ToString("X8"));
                WorkStage stage = new WorkStage();
                stage.id = reader.ReadUInt32();
                stage.size = reader.ReadUInt16();

                long startPos = reader.BaseStream.Position;

                switch (stage.id.ToString("X8"))
                {
                    case "8333A604":
                        stage.val = CDataEnumValue.Read(reader);
                        break;
                    case "B9187C94":
                        stage.val = CDataEnumValue.Read(reader);
                        break;
                    case "EC5D9069":
                        stage.val = CDataEnumValue.Read(reader);
                        break;
                    case "113F9CE1":
                        stage.productionStage = reader.ReadUInt32();
                        break;
                    case "5CA42B9D":
                        stage.val = CDataEnumValue.Read(reader);
                        break;
                    case "7B68B3A0":
                        stage.val = CDataEnumValue.Read(reader);
                        break;
                }

                // Guarantee stream alignment regardless of mapped IDs
                reader.BaseStream.Position = startPos + stage.size;
                return stage;
            }
        }

        public class CPerformanceGroupManagerResourceData
        {
            public CChunkDescriptor chunkDescriptor;
            public ushort count;
            public List<CScriptPerformanceGroupResourceData> data;

            public static CPerformanceGroupManagerResourceData Read(FileReader br)
            {
                CPerformanceGroupManagerResourceData resourceData = new CPerformanceGroupManagerResourceData();
                resourceData.chunkDescriptor = br.ReadStruct<CChunkDescriptor>();
                resourceData.count = br.ReadUInt16();
                resourceData.data = new List<CScriptPerformanceGroupResourceData>();
                if (resourceData.count > 0)
                {
                    for (int i = 0; i < resourceData.count; i++)
                    {
                        resourceData.data.Add(CScriptPerformanceGroupResourceData.Read(br));
                    }
                }

                return resourceData;
            }
        }

        public class CScriptPerformanceGroupResourceData // PGRP Entry
        {
            public string name;
            public CObjectId controllerID;
            public byte active;
            public ushort layerCount;
            public List<CObjectId> layerIds;

            public static CScriptPerformanceGroupResourceData Read(FileReader br)
            {
                CScriptPerformanceGroupResourceData data = new CScriptPerformanceGroupResourceData();
                data.layerIds = new List<CObjectId>();
                data.name = BinaryExtensions.ReadCStringFixed(br);
                data.controllerID = br.ReadStruct<CObjectId>();
                //Console.WriteLine("Controller ID Position: " + br.BaseStream.Position.ToString("X8"));
                data.active = br.ReadByte();
                data.layerCount = br.ReadUInt16();
                //Console.WriteLine("Performance Group Resource Data Count: " + data.count.ToString());
                if (data.layerCount > 0)
                {
                    for (int i = 0; i < data.layerCount; i++)
                    {
                        data.layerIds.Add(br.ReadStruct<CObjectId>());
                    }
                }
                return data;
            }
        }

        public class GeneratedObjectMap // LGEN
        {
            public CChunkDescriptor chunkDescriptor;
            public ushort count;
            public List<ObjectMapEntry> entries;

            public static GeneratedObjectMap Read(FileReader br)
            {
                //Console.WriteLine("Generated Object Map Position: " + br.BaseStream.Position.ToString("X8"));
                GeneratedObjectMap data = new GeneratedObjectMap();
                data.chunkDescriptor = br.ReadStruct<CChunkDescriptor>();
                data.count = br.ReadUInt16();
                data.entries = new List<ObjectMapEntry>();
                Console.WriteLine("Generated Object Map Count: " + data.count.ToString());
                if (data.count > 0)
                {
                    for (int i = 0; i < data.count; i++)
                    {
                        data.entries.Add(ObjectMapEntry.Read(br));
                        //Console.WriteLine("Object ID: " + data.entries[i].objectID);
                    }
                }
                return data;
            }
        }

        public class ObjectMapEntry // LGEN Entry
        {
            public CObjectId objectID;
            public CObjectId layerID;

            public static ObjectMapEntry Read(FileReader br)
            {
                return new ObjectMapEntry
                {
                    objectID = br.ReadStruct<CObjectId>(),
                    layerID = br.ReadStruct<CObjectId>()
                };
            }
        }

        public class DOCK // DOCK
        {
            public CChunkDescriptor chunkDescriptor;
            public ushort dockCount;
            public List<DockEntry> docks;

            public static DOCK Read(FileReader br)
            {
                //Console.WriteLine("Dock Position: " + br.BaseStream.Position.ToString("X8"));
                DOCK dockData = new DOCK();
                dockData.chunkDescriptor = br.ReadStruct<CChunkDescriptor>();
                dockData.dockCount = br.ReadUInt16();
                dockData.docks = new List<DockEntry>();
                Console.WriteLine("Dock Count: " + dockData.dockCount.ToString());
                if (dockData.dockCount > 0)
                {
                    for (int i = 0; i < dockData.dockCount; i++)
                    {
                        dockData.docks.Add(DockEntry.Read(br));
                    }
                }
                return dockData;
            }
        }

        public class DockEntry // DOCK Entry
        {
            public CObjectId a;
            public CObjectId b;
            public CObjectId c;
            public CObjectId d;
            public ushort roomTypeA;
            public ushort roomTypeB;

            public static DockEntry Read(FileReader br)
            {
                return new DockEntry
                {
                    a = br.ReadStruct<CObjectId>(),
                    b = br.ReadStruct<CObjectId>(),
                    c = br.ReadStruct<CObjectId>(),
                    d = br.ReadStruct<CObjectId>(),
                    roomTypeA = br.ReadUInt16(),
                    roomTypeB = br.ReadUInt16()
                };
            }
        }

        public class BakedLighting // BLIT
        {
            public CChunkDescriptor chunkDescriptor;
            public uint flags;
            public CObjectId lightMapTxtr;
            public uint idCount;
            public List<CObjectId> lightMapIds;
            public uint lookupCount;
            public List<SAtlasLookup> atlasLookups;
            public CObjectId lightProbeId;

            public static BakedLighting Read(FileReader br)
            {
                BakedLighting lightingData = new BakedLighting();
                lightingData.chunkDescriptor = br.ReadStruct<CChunkDescriptor>();
                lightingData.flags = br.ReadUInt32();
                if ((lightingData.flags & 1) != 0)
                {
                    lightingData.lightMapTxtr = br.ReadStruct<CObjectId>();
                    lightingData.idCount = br.ReadUInt32();
                    lightingData.lightMapIds = new List<CObjectId>();
                    if (lightingData.idCount > 0)
                    {
                        for (int i = 0; i < lightingData.idCount; i++)
                        {
                            lightingData.lightMapIds.Add(br.ReadStruct<CObjectId>());
                        }
                    }
                    lightingData.lookupCount = br.ReadUInt32();
                    lightingData.atlasLookups = new List<SAtlasLookup>();
                    if (lightingData.lookupCount > 0)
                    {
                        for (int i = 0; i < lightingData.lookupCount; i++)
                        {
                            lightingData.atlasLookups.Add(SAtlasLookup.Read(br));
                        }
                    }
                    lightingData.lightProbeId = br.ReadStruct<CObjectId>();
                    Console.WriteLine("Light Map ID: " + lightingData.lightMapTxtr);
                    Console.WriteLine("Light Probe ID: " + lightingData.lightProbeId);
                }
                return lightingData;
            }
        }

        public class CScriptLoadUnitResourceData
        {
            public CChunkDescriptor chunkDescriptor;
            public ushort loadUnitCount;
            public List<CScriptLoadUnit> loadUnits = new List<CScriptLoadUnit>();

            public static CScriptLoadUnitResourceData Read(FileReader reader)
            {
                //Console.WriteLine("LUNS location: " + br.BaseStream.Position.ToString("X8"));
                CScriptLoadUnitResourceData loadUnitResource = new CScriptLoadUnitResourceData();
                loadUnitResource.chunkDescriptor = reader.ReadStruct<CChunkDescriptor>();
                loadUnitResource.loadUnitCount = reader.ReadUInt16();

                for (int i = 0;i < loadUnitResource.loadUnitCount; i++)
                {
                    loadUnitResource.loadUnits.Add(CScriptLoadUnit.Read(reader));
                }

                return loadUnitResource;
            }
        }

        public class CScriptLoadUnit // LUNT
        {
            public CFormDescriptor formDescriptor;
            public LoadUnitHeader header;               // LUHD
            public LoadUnitResources resources;         // LRES
            public LoadUnitLayers layers;               // LLYR

            public static CScriptLoadUnit Read(FileReader br)
            {
                //Console.WriteLine("LUNT location: " + br.BaseStream.Position.ToString("X8"));
                CScriptLoadUnit unit = new CScriptLoadUnit();
                unit.formDescriptor = br.ReadStruct<CFormDescriptor>();
                unit.header = LoadUnitHeader.Read(br);
                unit.resources = LoadUnitResources.Read(br);
                unit.layers = LoadUnitLayers.Read(br);
                return unit;
            }
        }

        public class LoadUnitHeader
        {
            public CChunkDescriptor chunkDescriptor;
            public string name;
            public CObjectId id;
            public CObjectId id2;
            public ushort unk2;
            public uint unk3;
            public byte[] unkData2;
            public uint unk4;

            public static LoadUnitHeader Read(FileReader br)
            {
                LoadUnitHeader header = new LoadUnitHeader();
                header.chunkDescriptor = br.ReadStruct<CChunkDescriptor>();

                long endPos = br.BaseStream.Position + header.chunkDescriptor.DataSize;

                header.name = BinaryExtensions.ReadCStringFixed(br);
                header.id = br.ReadStruct<CObjectId>();
                header.id2 = br.ReadStruct<CObjectId>();
                header.unk2 = br.ReadUInt16();
                header.unk3 = br.ReadUInt32();
                Console.WriteLine("Load Unit Header unk3: " + header.unk3.ToString("X8"));
                if (header.unk3 > 0)
                {
                    header.unkData2 = br.ReadBytes((int)header.unk3);
                }
                header.unk4 = br.ReadUInt32();

                br.BaseStream.Position = endPos;
                return header;
            }
        }

        public class LoadUnitResources
        {
            public CChunkDescriptor chunkDescriptor;
            public uint resourceCount;
            public List<CObjectId> resourceIds;

            public static LoadUnitResources Read(FileReader br)
            {
                LoadUnitResources unitResources = new LoadUnitResources();
                unitResources.chunkDescriptor = br.ReadStruct<CChunkDescriptor>();
                unitResources.resourceCount = br.ReadUInt32();
                Console.WriteLine("Load Unit Resource Count: " + unitResources.resourceCount.ToString("X8"));
                if (unitResources.resourceCount > 0)
                {
                    unitResources.resourceIds = new List<CObjectId>();
                    for (int i = 0; i < unitResources.resourceCount; i++)
                    {
                        unitResources.resourceIds.Add(br.ReadStruct<CObjectId>());
                    }
                }

                return unitResources;
            }
        }

        public class LoadUnitLayers
        {
            public CChunkDescriptor chunkDescriptor;
            public uint layerCount;
            public List<CObjectId> layerIds;

            public static LoadUnitLayers Read(FileReader br)
            {
                LoadUnitLayers unitLayers = new LoadUnitLayers();
                unitLayers.chunkDescriptor = br.ReadStruct<CChunkDescriptor>();
                unitLayers.layerCount = br.ReadUInt32();
                unitLayers.layerIds = new List<CObjectId>();
                for (int i = 0; i < unitLayers.layerCount; i++)
                {
                    unitLayers.layerIds.Add(br.ReadStruct<CObjectId>());
                }
                return unitLayers;
            }
        }
        #endregion

        #region STRP Chunk
        public class StringPool()
        {
            public CChunkDescriptor chunkDescriptor;
            public uint unk1;
            public uint numStrings1;
            public List<Strings1> strings1;
            public uint unk2;
            public uint unk3;
            public uint poolLength;
            public byte[] poolData;

            public static StringPool Read(FileReader br)
            {
                StringPool pool = new StringPool();
                pool.chunkDescriptor = br.ReadStruct<CChunkDescriptor>();
                pool.unk1 = br.ReadUInt32();
                pool.numStrings1 = br.ReadUInt32();
                pool.strings1 = new List<Strings1>();
                for (int i = 0; i < pool.numStrings1; i++)
                {
                    Strings1 str1 = new Strings1();
                    str1.strLength = br.ReadUInt32();
                    str1.str = br.ReadBytes((int)str1.strLength);
                    pool.strings1.Add(str1);
                }
                pool.unk2 = br.ReadUInt32();
                pool.unk3 = br.ReadUInt32();
                pool.poolLength = br.ReadUInt32();
                pool.poolData = br.ReadBytes((int)pool.poolLength);
                return pool;
            }
        }

        public class Strings1
        {
            public uint strLength;
            public byte[] str;
        }
        #endregion

        #region DATA Chunk

        public class ScriptDataChunk()
        {
            public CFormDescriptor formDescriptor;
            public ScriptDataHeader Header;
            public List<ScriptDataEntity> entities;            // SDEN
            public List<SGOComponentInstanceData> InstanceData;   // IDTA

            public static ScriptDataChunk Read(FileReader br)
            {
                Console.WriteLine("SDTA location: " + br.BaseStream.Position.ToString("X8"));
                ScriptDataChunk data = new ScriptDataChunk();
                data.formDescriptor = br.ReadStruct<CFormDescriptor>();
                data.Header = ScriptDataHeader.Read(br);

                data.entities = new List<ScriptDataEntity>();
                //Console.WriteLine("Entity Data location: " + br.BaseStream.Position.ToString("X8"));
                for (int i = 0; i < data.Header.propertiesCount; i++)               // SDENs
                {
                    //Console.WriteLine("Current SDEN location: " + br.BaseStream.Position.ToString("X8"));
                    data.entities.Add(ScriptDataEntity.Read(br));
                }

                data.InstanceData = new List<SGOComponentInstanceData>();
                //Console.WriteLine("Instance Data location: " + br.BaseStream.Position.ToString("X8"));
                for (int i = 0; i < data.Header.instanceDataCount; i++)             // IDTAs
                {
                    //Console.WriteLine("Current IDTA location: " + br.BaseStream.Position.ToString("X8"));
                    data.InstanceData.Add(SGOComponentInstanceData.Read(br));
                }
                return data;
            }
        }

        public class ScriptDataHeader               // SDHR
        {
            public CChunkDescriptor chunkDescriptor;
            public uint propertiesCount;
            public uint instanceDataCount;
            public uint dataLength;
            public List<CObjectId> ids;
            public List<uint> propertiesIndex;
            public List<uint> instancesIndex;

            public static ScriptDataHeader Read(FileReader br)
            {
                ScriptDataHeader dataHeader = new ScriptDataHeader();
                dataHeader.chunkDescriptor = br.ReadStruct<CChunkDescriptor>();
                dataHeader.propertiesCount = br.ReadUInt32();
                dataHeader.instanceDataCount = br.ReadUInt32();
                dataHeader.dataLength = br.ReadUInt32();
                dataHeader.ids = new List<CObjectId>();

                for (int i = 0; i < dataHeader.dataLength; i++)
                {
                    dataHeader.ids.Add(br.ReadStruct<CObjectId>());
                }

                dataHeader.propertiesIndex = new List<uint>();
                for (int i = 0; i < dataHeader.dataLength; i++)
                {
                    dataHeader.propertiesIndex.Add(br.ReadUInt32());
                }

                dataHeader.instancesIndex = new List<uint>();
                for (int i = 0; i < dataHeader.dataLength; i++)
                {
                    dataHeader.instancesIndex.Add(br.ReadUInt32());
                }
                return dataHeader;
            }
        }

        public class ScriptDataEntity          // SDEN
        {
            public CChunkDescriptor chunkDescriptor;
            public EGOComponentType typeId;
            public byte[] propertyData; // Variable data that will need to be parsed based on the type.

            public static ScriptDataEntity Read(FileReader br)
            {
                ScriptDataEntity entity = new ScriptDataEntity();
                entity.chunkDescriptor = br.ReadStruct<CChunkDescriptor>();
                entity.typeId = (EGOComponentType)br.ReadUInt32();
                entity.propertyData = br.ReadBytes((int)entity.chunkDescriptor.DataSize - 4); // Subtract 4 bytes for the ComponentType that was just read

                return entity;
            }
        }

        public class ScriptDataEntityProperty
        {
            uint propertyID;
            ushort propertySize;
            byte[] propertyData;

            public static ScriptDataEntityProperty Read(FileReader br)
            {
                ScriptDataEntityProperty property = new ScriptDataEntityProperty();
                property.propertyID = br.ReadUInt32();
                property.propertySize = br.ReadUInt16();
                property.propertyData = br.ReadBytes(property.propertySize);
                return property;
            }
        }

        public class SGOComponentInstanceData // IDTA
        {
            public CChunkDescriptor chunkDescriptor;
            public CObjectId id;
            public PooledString str;
            public ushort connectionCount;
            public List<SConnection> connections;
            public ushort linkCount;
            public List<SScriptLink> links;

            public static SGOComponentInstanceData Read(FileReader br)
            {
                SGOComponentInstanceData component = new SGOComponentInstanceData();
                //Console.WriteLine("Component location: " + br.BaseStream.Position.ToString("X8"));
                component.chunkDescriptor = br.ReadStruct<CChunkDescriptor>();

                long startPos = br.BaseStream.Position;

                component.id = br.ReadStruct<CObjectId>();
                component.str = PooledString.Read(br);
                component.connectionCount = br.ReadUInt16();
                component.connections = new List<SConnection>();
                if(component.connectionCount > 0)
                {
                    for (int i = 0; i < component.connectionCount; i++)
                    {
                        component.connections.Add(SConnection.Read(br));
                    }
                }
                component.linkCount = br.ReadUInt16();
                component.links = new List<SScriptLink>();
                if( component.linkCount > 0)
                {
                    for (int i = 0; i < component.linkCount; i++)
                    {
                        component.links.Add(SScriptLink.Read(br));
                    }
                }

                br.Position = startPos + component.chunkDescriptor.DataSize;        // Guarentees the reader will finish.

                return component;
            }
        }

        public class SConnection
        {
            public uint connectionEvent;
            public uint connectionAction;
            public CObjectId target;
            public ushort unkU16;
            public SizeofAllocations eventCriteriaSldr;
            public SizeofAllocations actionPayloadSldr;
            public byte[] flagBytes = new byte[3];
            public CObjectId trailingId;

            public static SConnection Read(FileReader br)
            {
                SConnection connection = new SConnection();
                connection.connectionEvent = br.ReadUInt32();
                connection.connectionAction = br.ReadUInt32();
                connection.target = br.ReadStruct<CObjectId>();
                connection.unkU16 = br.ReadUInt16();
                connection.eventCriteriaSldr = SizeofAllocations.Read(br);
                connection.actionPayloadSldr = SizeofAllocations.Read(br);
                connection.flagBytes = br.ReadBytes(3);
                connection.trailingId = br.ReadStruct<CObjectId>();
                return connection;
            }
        }

        public class SScriptLink
        {
            public Magic link;
            public CObjectId target;
            public SizeofAllocations data;
            public byte[] flagBytes = new byte[2];
            public CObjectId trailingId;

            public static SScriptLink Read(FileReader br)
            {
                SScriptLink link = new SScriptLink();
                link.link = br.ReadStruct<Magic>();
                link.target = br.ReadStruct<CObjectId>();
                link.data = SizeofAllocations.Read(br);
                link.flagBytes = br.ReadBytes(2);
                link.trailingId = br.ReadStruct<CObjectId>();
                return link;
            }
        }

        public class PooledString
        {
            public int a;
            public uint b;
            public byte[] Skip;

            public static PooledString Read(FileReader br)
            {
                PooledString str = new PooledString();
                str.a = br.ReadInt32();
                str.b = br.ReadUInt32();
                if (str.a == -1 && str.b > 0)
                {
                    str.Skip = br.ReadBytes((int)str.b);
                }
                return str;
            }
        }

        public struct SizeofAllocations
        {
            public uint a;
            public byte[] Skip;
            public byte[] Skip2;

            public static SizeofAllocations Read(FileReader br)
            {
                SizeofAllocations alloc = new SizeofAllocations();
                alloc.a = br.ReadUInt32();
                if (alloc.a != 0)
                {
                    ushort size = br.ReadUInt16();
                    if (size > 0)
                    {
                        alloc.Skip = br.ReadBytes((int)size);
                    }
                    uint size2 = br.ReadUInt32();
                    if (size2 > 0)
                    {
                        alloc.Skip2 = br.ReadBytes((int)size2);
                    }
                }
                return alloc;
            }
        }

        #endregion

        #region LYRS Chunk
        public class RoomLayersChunk()
        {
            public CFormDescriptor formDescriptor;
            public List<Layer> layers;

            public static RoomLayersChunk Read(FileReader br)
            {
                RoomLayersChunk layers = new RoomLayersChunk();
                layers.formDescriptor = br.ReadStruct<CFormDescriptor>();
                long posBeforeRead = br.BaseStream.Position;
                if (layers.formDescriptor.DataSize > 0)
                {
                    layers.layers = new List<Layer>();
                    while ((br.BaseStream.Position - posBeforeRead) < (long)layers.formDescriptor.DataSize)
                    {
                        layers.layers.Add(Layer.Read(br));
                    }
                }
                return layers;
            }
        }

        public class Layer
        {
            public CFormDescriptor formDescriptor;
            public LayerHeader layerHeader;
            public GeneratedGameObjectResources gameObjectResources; // GSRP
            public SRIP srip;

            public static Layer Read(FileReader br)
            {
                Layer layer = new Layer();
                //Console.WriteLine("Layer Form location: " + br.BaseStream.Position.ToString("X8"));
                layer.formDescriptor = br.ReadStruct<CFormDescriptor>();
                //Console.WriteLine("Layer Header location: " + br.BaseStream.Position.ToString("X8"));
                layer.layerHeader = LayerHeader.Read(br);
                //Console.WriteLine("Object Resources location: " + br.BaseStream.Position.ToString("X8"));
                layer.gameObjectResources = GeneratedGameObjectResources.Read(br);
                //Console.WriteLine("SRIP location: " + br.BaseStream.Position.ToString("X8"));
                layer.srip = SRIP.Read(br);

                return layer;
            }
        }

        public class LayerHeader
        {
            public CChunkDescriptor chunkDescriptor;
            public string name;
            public CObjectId Id;
            public ushort entityCount;
            public ushort generatedObjectCount;
            public ushort loadUnitCount;
            public List<CObjectId> loadUnitIds;
            public CObjectId reloadSetId;
            public byte resetOnActivation;

            public static LayerHeader Read(FileReader br)
            {
                LayerHeader header = new LayerHeader();
                header.chunkDescriptor = br.ReadStruct<CChunkDescriptor>();
                header.name = BinaryExtensions.ReadCStringFixed(br);
                header.Id = br.ReadStruct<CObjectId>();
                header.entityCount = br.ReadUInt16();
                header.generatedObjectCount = br.ReadUInt16();
                header.loadUnitCount = br.ReadUInt16();
                header.loadUnitIds = new List<CObjectId>();
                for (int i = 0; i < (header.loadUnitCount); i++)
                {
                    header.loadUnitIds.Add(br.ReadStruct<CObjectId>());
                }
                header.reloadSetId = br.ReadStruct<CObjectId>();
                header.resetOnActivation = br.ReadByte();
                return header;
            }
        }

        public class GeneratedGameObjectResources               // GSRP
        {
            public CFormDescriptor formDescriptor;
            public List<GeneratedGameObject> generatedGameObjects;

            public static GeneratedGameObjectResources Read(FileReader br)
            {
                GeneratedGameObjectResources gameObjectResources = new GeneratedGameObjectResources();
                gameObjectResources.formDescriptor = br.ReadStruct<CFormDescriptor>();
                long posBeforeRead = br.BaseStream.Position;
                gameObjectResources.generatedGameObjects = new List<GeneratedGameObject>();
                if (gameObjectResources.formDescriptor.DataSize > 0)
                {
                    while ((br.BaseStream.Position - posBeforeRead) < (long)gameObjectResources.formDescriptor.DataSize)
                    {
                        gameObjectResources.generatedGameObjects.Add(GeneratedGameObject.Read(br));
                    }
                }
                //Console.WriteLine("Number of GGOB: " + gameObjectResources.GGOB.Count.ToString("X8"));
                return gameObjectResources;
            }
        }

        public class GeneratedGameObject                        // GGOB
        {
            public CChunkDescriptor chunkDescriptor;
            public CObjectId id;
            public ushort count;
            public List<CGameObjectComponent> components;

            public static GeneratedGameObject Read(FileReader br)
            {
                GeneratedGameObject gameObject = new GeneratedGameObject();
                gameObject.chunkDescriptor = br.ReadStruct<CChunkDescriptor>();
                gameObject.id = br.ReadStruct<CObjectId>();
                gameObject.count = br.ReadUInt16();
                gameObject.components = new List<CGameObjectComponent>();
                for (int i = 0; i < gameObject.count; i++)
                {
                    gameObject.components.Add(CGameObjectComponent.Read(br));
                }
                //Console.WriteLine("Number of GeneratedGameObject components: " + gameObject.components.Count.ToString("X8"));
                return gameObject;
            }
        }

        public class SRIP                                       // SRIP
        {
            public CFormDescriptor formDescriptor;
            public Components compChunk;

            public static SRIP Read(FileReader br)
            {
                SRIP srip = new SRIP();
                srip.formDescriptor = br.ReadStruct<CFormDescriptor>();
                srip.compChunk = Components.Read(br);
                return srip;
            }
        }

        public class Components                                 // COMP
        {
            public CChunkDescriptor chunkDescriptor;
            public List<CGameObjectComponent> components;

            public static Components Read(FileReader br)
            {
                Components comp = new Components();
                comp.chunkDescriptor = br.ReadStruct<CChunkDescriptor>();

                comp.components = new List<CGameObjectComponent>();

                if (comp.chunkDescriptor.DataSize > 0)
                {
                    long posBeforeReading = br.BaseStream.Position;
                    while ((br.BaseStream.Position - posBeforeReading) < (long)comp.chunkDescriptor.DataSize)
                    {
                        comp.components.Add(CGameObjectComponent.Read(br));
                    }
                }
                return comp;
            }
        }

        public class CGameObjectComponent
        {
            public uint type;
            public uint propertyIndex;
            public uint instanceIndex;

            public static CGameObjectComponent Read(FileReader br)
            {
                CGameObjectComponent properties = new CGameObjectComponent();
                properties.type = br.ReadUInt32();
                properties.propertyIndex = br.ReadUInt32();
                properties.instanceIndex = br.ReadUInt32();
                return properties;
            }
        }
        #endregion

        #region Room Meta Data
        public class RoomMetaData
        {
            public ushort version;
            public ERoomType type;
            public Magic game;
            public CObjectId parentRoomId;
            public ushort unk1;
            public byte[] unk2 = new byte[3];
            public uint persistenceStateCount;
            public List<SGameAreaPersistenceState> states = new List<SGameAreaPersistenceState>();

            public CAABox bounds;
            public uint loadUnitMetaCount;
            public List<SScriptLoadUnitMetaData> loadUnitMetaData = new List<SScriptLoadUnitMetaData>();

            public ushort envVarCount;
            public List<SEnvironmentVar> envVar = new List<SEnvironmentVar>();

            public uint unkMetaStructCount;
            public List<unkMetaStruct> unkMetaStructs = new List<unkMetaStruct>();

            public uint byteCount;
            public byte[] data;
        }

        public class SGameAreaPersistenceState()
        {
            public CObjectId id;
            public string name;
            public CObjectId id2;
            public CObjectId id3;
            public byte flag0;
            public byte flag1;
            public uint envVarCount;
            public List<SGameAreaPersistenceStateEnvVar> envVar = new List<SGameAreaPersistenceStateEnvVar>();

            public static SGameAreaPersistenceState Read(FileReader reader)
            {
                SGameAreaPersistenceState data = new SGameAreaPersistenceState();
                data.id = reader.ReadStruct<CObjectId>();
                data.name = BinaryExtensions.ReadCStringFixed(reader);
                data.id2 = reader.ReadStruct<CObjectId>();
                data.id3 = reader.ReadStruct<CObjectId>();
                data.flag0 = reader.ReadByte();
                data.flag1 = reader.ReadByte();
                data.envVarCount = reader.ReadUInt32();
                data.envVar.Add(SGameAreaPersistenceStateEnvVar.Read(reader));
                return data;
            }
        }

        public class SGameAreaPersistenceStateEnvVar
        {
            public string name;
            public uint unk;

            public static SGameAreaPersistenceStateEnvVar Read(FileReader reader)
            {
                SGameAreaPersistenceStateEnvVar data = new SGameAreaPersistenceStateEnvVar();
                data.name = BinaryExtensions.ReadCStringFixed(reader);
                data.unk = reader.ReadUInt32();
                return data;
            }
        }

        public class SScriptLoadUnitMetaData
        {
            public CObjectId id;
            public CObjectId stateVariableId;
            public ushort flags;
            public uint byteCount;
            public LoadUnitLogic logic;

            public static SScriptLoadUnitMetaData Read(FileReader reader)
            {
                SScriptLoadUnitMetaData lunt = new SScriptLoadUnitMetaData();
                lunt.id = reader.ReadStruct<CObjectId>();
                lunt.stateVariableId = reader.ReadStruct<CObjectId>();
                lunt.flags = reader.ReadUInt16();
                lunt.byteCount = reader.ReadUInt32();
                if(lunt.byteCount > 0)
                {
                    lunt.logic = LoadUnitLogic.Read(reader, lunt.byteCount);
                }
                return lunt;
            }
        }

        public class SEnvironmentVar
        {
            public CObjectId id;
            public string name;
            public uint byteCount;
            public byte[] data;
            public int initalValue;
            public uint flags;
            public uint unkFlag0;
            public uint unkFlag1;

            public static SEnvironmentVar Read(FileReader reader)
            {
                SEnvironmentVar data = new SEnvironmentVar();
                data.id = reader.ReadStruct<CObjectId>();
                data.name = BinaryExtensions.ReadCStringFixed(reader);
                data.byteCount = reader.ReadUInt32();
                data.data = reader.ReadBytes((int)data.byteCount);
                data.initalValue = reader.ReadInt32();
                data.flags = reader.ReadUInt32();
                if ((data.flags & 1) != 0)
                {
                    data.unkFlag0 = reader.ReadUInt32();
                }
                if ((data.flags & 2) != 0)
                {
                    data.unkFlag1 = reader.ReadUInt32();
                }
                return data;
            }

        }

        public class unkMetaStruct
        {
            public CObjectId id;
            public uint unk;
            public string name;

            public static unkMetaStruct Read(FileReader reader)
            {
                unkMetaStruct data = new unkMetaStruct();
                data.id = reader.ReadStruct<CObjectId>();
                data.unk = reader.ReadUInt32();
                data.name = BinaryExtensions.ReadCStringFixed(reader);
                return data;
            }
        }
        #endregion
    }



    public static class BinaryExtensions
    {
        public static string ReadCStringFixed(BinaryReader br)
        {
            uint size = br.ReadUInt32();
            //Console.WriteLine("String Length: " + size.ToString("X8"));
            if (size > 0)
            {
                char[] returnString = new char[size];
                for (int i = 0; i < size; i++)
                {
                    returnString[i] = br.ReadChar();
                }
                return new string(returnString);
            }
            return string.Empty;
        }
    }

}

