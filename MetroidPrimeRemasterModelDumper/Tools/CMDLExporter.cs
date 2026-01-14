using AvaloniaToolbox.Core;
using DKCTF;
using IONET;
using IONET.Collada.Core.Scene;
using IONET.Core;
using IONET.Core.Model;
using IONET.Core.Skeleton;
using RetroStudioPlugin.Files.FileData;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

#nullable disable

namespace EvilWithin2Tool
{
    public class CMDLExporter
    {
        public static void Export(CMDL cmdl, string path, CHPR charProject = null)
        {
            IOScene ioscene = new IOScene();

            IOModel iomodel = new IOModel();
            ioscene.Models.Add(iomodel);

            List<IOMaterial> materials = new List<IOMaterial>();
            foreach (var mat in cmdl.Materials)
            {
                materials.Add(new IOMaterial()
                {
                    Name = mat.Name,
                    Label = mat.Name,
                });
            }
            ioscene.Materials.AddRange(materials);

            if (charProject != null)
            {
                List<IOBone> iobones = new List<IOBone>();
                foreach (var bone in charProject.CharacterInfos[0].Bones)
                {
                    Matrix4x4.Decompose(bone.LocalTransform,
                        out Vector3 scale,
                        out Quaternion rotation,
                        out Vector3 translation);

                    iobones.Add(new IOBone()
                    {
                        Name = bone.Name,
                        Rotation = rotation,
                        Scale = scale,
                        Translation = translation,
                    });
                }
                for (int i = 0; i < iobones.Count; i++)
                {
                    var parentName = charProject.CharacterInfos[0].Bones[i].Parent;

                    var ioparent = iobones.FirstOrDefault(x => x.Name == parentName);
                    if (ioparent != null)
                        ioparent.AddChild(iobones[i]);
                }

                foreach (var iobone in iobones)
                    if (iobone.Parent == null)
                        iomodel.Skeleton.RootBones.Add(iobone);
            }
            else
            {
                iomodel.Skeleton.RootBones.Add(new IOBone()
                {
                    Name = "Root",
                });
            }

            var lods = new List<ModelLod>();
            for (int lodIndex = 0; lodIndex < cmdl.lods.Count(); lodIndex++)
            {
                var lod = cmdl.lods[lodIndex];

                foreach (var inner in lod.inner)
                {
                    int offset = (int)inner.offset;
                    int end = (int)offset + (int)inner.count;

                    for (int s = offset; s < end; s++)
                    {
                        int meshIndex = cmdl.shorts[s];
                        cmdl.Meshes[meshIndex].parentLOD = lodIndex;
                    }
                }
            }

            int MatName = 0;
            foreach (var mesh in cmdl.Meshes)
            {
                //var mat = cmdl.Materials[mesh.Header.MaterialIndex];

                IOMesh iomesh = new IOMesh();
                //iomesh.Name = $"Mesh{iomodel.Meshes.Count}_{mat.Name}";
                iomesh.Name = $"Mesh{iomodel.Meshes.Count}_LOD{mesh.parentLOD}";
                iomodel.Meshes.Add(iomesh);

                foreach (var vert in mesh.Vertices)
                {
                    var iovertex = new IOVertex()
                    {
                        Position = new System.Numerics.Vector3(
                            vert.Position.X,
                            vert.Position.Y,
                            vert.Position.Z), 
                        Normal = new System.Numerics.Vector3(
                            vert.Normal.X,
                            vert.Normal.Y,
                            vert.Normal.Z),
                        Tangent = new System.Numerics.Vector3(
                            vert.Tangent.X, 
                            vert.Tangent.Y,
                            vert.Tangent.Z),
                    };
                    iomesh.Vertices.Add(iovertex);

                    iovertex.SetUV(vert.TexCoord0.X, vert.TexCoord0.Y, 0);
                    iovertex.SetUV(vert.TexCoord1.X, vert.TexCoord1.Y, 1);
                    iovertex.SetUV(vert.TexCoord2.X, vert.TexCoord2.Y, 2);

                    iovertex.SetColor(
                        vert.Color.X,
                        vert.Color.Y,
                        vert.Color.Z,
                        vert.Color.W, 0);

                    for (int j = 0; j < 4; j++)
                    {
                        if (vert.BoneWeights[j] == 0 || charProject == null)
                            continue;

                        var boneIdx = (int)vert.BoneIndices[j];
                        var boneName = charProject.CharacterInfos[0].SkinnedBones[boneIdx];

                        iovertex.Envelope.Weights.Add(new IOBoneWeight()
                        {
                            BoneName = boneName,
                            Weight = vert.BoneWeights[j],
                        });
                    }
                    iovertex.SetColor(vert.Color.X,
                                      vert.Color.Y,
                                      vert.Color.Z,
                                      vert.Color.W, 0);

                    iovertex.Envelope.NormalizeByteType();
                }

                IOPolygon iopoly = new IOPolygon();
                iomesh.Polygons.Add(iopoly);

                iopoly.MaterialName = "Material_" + MatName++;

                iomesh.TransformVertices(Matrix4x4.Identity);

                for (int i = 0; i < mesh.Indices.Length; i++)
                    iopoly.Indicies.Add((int)mesh.Indices[i]);
            }



            IOManager.ExportScene(ioscene, path, new ExportSettings()
            {
            });
        }
    }

    class ModelLod
    {
        public BitArray Meshes;
        public float? Distance;
    }
}
