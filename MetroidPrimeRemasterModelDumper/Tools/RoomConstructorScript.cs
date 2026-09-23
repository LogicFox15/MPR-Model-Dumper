using IONET.Collada.Kinematics.Articulated_Systems;
using RetroStudioPlugin.Files.FileData;
using MetroidPrimeRemasterModelDumper.ScriptTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DKCTF;

namespace MetroidPrimeRemasterModelDumper.Tools
{
    public class ConstructedRoom
    {
        public List<ConstructedLayer> layers = new List<ConstructedLayer>();
        public CObjectId lightMapTxtr;
        public List<CObjectId> lightMapIds = new List<CObjectId>();
        public List<SAtlasLookup> lightMapAtlasLookups = new List<SAtlasLookup>();


        public static ConstructedRoom ProcessRoomForConstruction(ROOM room)
        {
            ConstructedRoom newRoom = new ConstructedRoom();

            if (room.HeadChunk.bakedLighting != null)
            {
                newRoom.lightMapTxtr =
                    room.HeadChunk.bakedLighting.lightMapTxtr;

                if (room.HeadChunk.bakedLighting.lightMapIds != null)
                {
                    newRoom.lightMapIds.AddRange(
                        room.HeadChunk.bakedLighting.lightMapIds);
                }

                if (room.HeadChunk.bakedLighting.atlasLookups != null)
                {
                    newRoom.lightMapAtlasLookups.AddRange(
                        room.HeadChunk.bakedLighting.atlasLookups);
                }
            }


            for (int i = 0; i < room.LayersChunk.layers.Count; i++)
            {
                ConstructedLayer constructedLayer = new ConstructedLayer();

                for (int c = 0; c < room.LayersChunk.layers[i].srip.compChunk.components.Count; c++)
                {
                    var component = room.LayersChunk.layers[i].srip.compChunk.components[c];
                    var entity = room.ScriptData.entities[(int)component.propertyIndex];
                    var instance = room.ScriptData.InstanceData[(int)component.instanceIndex];

                    switch ((EGOComponentType)room.LayersChunk.layers[i].srip.compChunk.components[c].type)
                    {
                        case EGOComponentType.EntityProperties:
                            constructedLayer.entityProperties.Add(EntityProperties.Build(component, entity, instance));
                            break;
                        case EGOComponentType.RoomSettings:
                            Console.WriteLine("Found Room Settings");
                            constructedLayer.roomSettings.Add(RoomSettings.Build(component, entity, instance));
                            break;
                        case EGOComponentType.RoomController:
                            Console.WriteLine("Found Room Controller");
                            constructedLayer.roomControllers.Add(RoomController.Build(component, entity, instance));
                            break;
                        case EGOComponentType.BakedLightingPriorityModifierMP1:
                            Console.WriteLine("Found Baked Lighting Priority Modifier MP1");
                            constructedLayer.BakedLightingPriorityModifierMP1s.Add(BakedLightingPriorityModifierMP1.Build(component, entity, instance));
                            break;
                        case EGOComponentType.LightStatic:
                            Console.WriteLine("Found Light Static");
                            constructedLayer.lightStatics.Add(LightStatic.Build(component, entity, instance));
                            break;
                        case EGOComponentType.ColorModifier:
                            Console.WriteLine("Found Color Modifier");
                            constructedLayer.colorModifiers.Add(ColorModifier.Build(component, entity, instance));
                            break;
                        case EGOComponentType.ModCon:
                            Console.WriteLine("Found ModCon");
                            constructedLayer.modCons.Add(ModConScript.Build(component, entity, instance));
                            break;
                    }
                }

                newRoom.layers.Add(constructedLayer);
            }

            return newRoom;


            // Clear before processing another room
            // layers = new List<ConstructedLayer>();
        }
    }

    public class ConstructedLayer
    {
        public List<EntityProperties> entityProperties = new List<EntityProperties>();
        public List<RoomSettings> roomSettings = new List<RoomSettings>();
        public List<RoomController> roomControllers = new List<RoomController>();
        public List<BakedLightingPriorityModifierMP1> BakedLightingPriorityModifierMP1s = new List<BakedLightingPriorityModifierMP1>();
        public List<LightStatic> lightStatics = new List<LightStatic>();
        public List<ColorModifier> colorModifiers = new List<ColorModifier>();
        public List<ModConScript> modCons = new List<ModConScript>();
    }

    public class ModularConstructionContainer
    {
        public List<CMDL> models = new List<CMDL>();
    }

}
