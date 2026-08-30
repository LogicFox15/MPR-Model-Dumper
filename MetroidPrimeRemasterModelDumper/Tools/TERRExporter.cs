using AvaloniaToolbox.Core;
using DKCTF;
using RetroStudioPlugin.Files.FileData;
using System;
using System.IO;
using System.Linq;
using MetroidPrimeRemasterModelDumper;
using static DKCTF.TERR;
using static ImageLibrary.GenericTextureBase;
using System.Drawing;
using System.Drawing.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using IONET.Collada.Core.Lighting;
using Newtonsoft.Json.Linq;
using IONET.Core.Model;
using IONET.Core.Skeleton;
using IONET.Core;
using IONET;
using System.Numerics;
using static AvaloniaToolbox.Core.ConsoleLogger;
using static System.Net.WebRequestMethods;

#nullable disable

namespace EvilWithin2Tool
{
    public class TERRExporter
    {
        public static void ExportObj(TERR terr, string folder, PAK pak)
        {
            var fieldDimensions = terr.Header.VertexStreamDescription.Dimensions[0].X * terr.Header.VertexStreamDescription.Dimensions[0].Y;
            var gridWidth = (int)terr.Header.VertexStreamDescription.Dimensions[0].X;
            int gridSize = (int)terr.Header.VertexStreamDescription.TileDimensions;

            for (int i = 0; i < fieldDimensions; i++)
            {
                int column = i % gridWidth;
                int row = i / gridWidth;

                string currentFolder = Path.Combine(Path.GetFileNameWithoutExtension(pak.FileInfo.FilePath), folder);
                if (!Directory.Exists(currentFolder))
                    Directory.CreateDirectory(currentFolder);

                // Change extension to .obj
                string modelName = $"{row}_{column}.obj";
                string path = Path.Combine(currentFolder, modelName);

                TerrainTile tile = terr.tiles[i];

                if(column > 20 && column < 66 && row > 20 && row < 66)
                {
                    // Stream directly to the file to save RAM
                    using (StreamWriter writer = new StreamWriter(path))
                    {
                        writer.WriteLine($"o Tile_{row}_{column}");

                        // 1. Write all vertices
                        foreach (var vert in tile.tileVerts)
                        {
                            // Use InvariantCulture to ensure periods are used for decimals, not commas
                            writer.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                "v {0:F6} {1:F6} {2:F6}",
                                vert.Position.X, vert.Position.Y, vert.Position.Z));
                        }

                        // 2. Write all faces (OBJ indices start at 1, not 0)
                        for (int z = 0; z < gridSize - 1; z++)
                        {
                            for (int x = 0; x < gridSize - 1; x++)
                            {
                                int topLeft = (z * gridSize + x) + 1;
                                int topRight = topLeft + 1;
                                int bottomLeft = ((z + 1) * gridSize + x) + 1;
                                int bottomRight = bottomLeft + 1;

                                // Triangle 1
                                writer.WriteLine($"f {topLeft} {bottomLeft} {topRight}");
                                // Triangle 2
                                writer.WriteLine($"f {topRight} {bottomLeft} {bottomRight}");
                            }
                        }
                    }
                }
                
            }
        }

        public static void ExportObjFixed(TERR terr, string folder, PAK pak)
        {
            var fieldDimensions = terr.Header.VertexStreamDescription.Dimensions[0].X * terr.Header.VertexStreamDescription.Dimensions[0].Y;
            var gridWidth = (int)terr.Header.VertexStreamDescription.Dimensions[0].X;
            var gridHeight = (int)terr.Header.VertexStreamDescription.Dimensions[0].Y;
            int gridSize = (int)terr.Header.VertexStreamDescription.TileDimensions;

            for (int i = 0; i < fieldDimensions; i++)
            {
                int column = i % gridWidth;
                int row = i / gridWidth;

                string baseFolder = Path.Combine(Path.GetFileNameWithoutExtension(pak.FileInfo.FilePath), folder);
                string currentFolder = "";

                if (row < 17)
                {
                    currentFolder = Path.Combine(baseFolder, "Group_1_1");
                    if (column > 16)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_1_2");
                    }
                    if (column > 33)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_1_3");
                    }
                    if (column > 50)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_1_4");
                    }
                    if (column > 67)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_1_5");
                    }
                }
                if (row > 16)
                {
                    currentFolder = Path.Combine(baseFolder, "Group_2_1");
                    if (column > 16)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_2_2");
                    }
                    if (column > 33)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_2_3");
                    }
                    if (column > 50)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_2_4");
                    }
                    if (column > 67)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_2_5");
                    }
                }
                if (row > 33)
                {
                    currentFolder = Path.Combine(baseFolder, "Group_3_1");
                    if (column > 16)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_3_2");
                    }
                    if (column > 33)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_3_3");
                    }
                    if (column > 50)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_3_4");
                    }
                    if (column > 67)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_3_5");
                    }
                }
                if (row > 50)
                {
                    currentFolder = Path.Combine(baseFolder, "Group_4_1");
                    if (column > 16)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_4_2");
                    }
                    if (column > 33)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_4_3");
                    }
                    if (column > 50)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_4_4");
                    }
                    if (column > 67)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_4_5");
                    }
                }
                if (row > 67)
                {
                    currentFolder = Path.Combine(baseFolder, "Group_5_1");
                    if (column > 16)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_5_2");
                    }
                    if (column > 33)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_5_3");
                    }
                    if (column > 50)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_5_4");
                    }
                    if (column > 67)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_5_5");
                    }
                }

                if (!Directory.Exists(currentFolder))
                {
                    Directory.CreateDirectory(currentFolder);
                }

                string path = Path.Combine(currentFolder, $"{row}_{column}.obj");

                // Determine if we have neighbors to bridge gaps with
                bool hasRight = column < gridWidth - 1;
                bool hasBottom = row < gridHeight - 1;

                // Extend the vertex grid by 1 if there is a neighbor
                int vertsX = hasRight ? gridSize + 1 : gridSize;
                int vertsZ = hasBottom ? gridSize + 1 : gridSize;

                


                using (StreamWriter writer = new StreamWriter(path))
                {
                    writer.WriteLine($"o Tile_{row}_{column}");

                    // 1. Write vertices (including borrowed neighbor edges)
                    for (int z = 0; z < vertsZ; z++)
                    {
                        for (int x = 0; x < vertsX; x++)
                        {
                            int srcRow = row;
                            int srcCol = column;
                            int srcX = x;
                            int srcZ = z;

                            float offsetX = 0f;
                            float offsetZ = 0f;

                            // If we hit the extended right edge, grab from the Right neighbor
                            if (x == gridSize)
                            {
                                srcCol = column + 1;
                                srcX = 0;
                                offsetX = gridSize; // Shift local coordinate by the tile width
                            }

                            // If we hit the extended bottom edge, grab from the Bottom neighbor
                            if (z == gridSize)
                            {
                                srcRow = row + 1;
                                srcZ = 0;

                                // FIX: Must be negative to match TERR.cs vertex generation
                                offsetZ = -gridSize;
                            }

                            TerrainTile srcTile = terr.tiles[srcRow * gridWidth + srcCol];
                            var vert = srcTile.tileVerts[(srcZ * gridSize) + srcX];

                            // Add offsets to put the borrowed vertices in the current tile's local space
                            float finalX = vert.Position.X + offsetX;
                            float finalY = vert.Position.Y;
                            float finalZ = vert.Position.Z + offsetZ;

                            writer.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                "v {0:F6} {1:F6} {2:F6}",
                                finalX, finalY, finalZ));

                            int test = vert.textureUsage;

                            float u = test;
                            float v = 1;

                            // 3. Write the texture coordinate
                            writer.WriteLine($"vt {u} {v}");
                        }
                    }

                    // 2. Write faces out to the new extended edges
                    for (int z = 0; z < vertsZ - 1; z++)
                    {
                        for (int x = 0; x < vertsX - 1; x++)
                        {
                            int topLeft = (z * vertsX + x) + 1;
                            int topRight = topLeft + 1;
                            int bottomLeft = ((z + 1) * vertsX + x) + 1;
                            int bottomRight = bottomLeft + 1;

                            writer.WriteLine($"f {topLeft} {bottomLeft} {topRight}");
                            writer.WriteLine($"f {topRight} {bottomLeft} {bottomRight}");
                        }
                    }
                }
            }
        }

        public static void ExportGLTF(TERR terr, string folder, PAK pak)
        {
            var fieldDimensions = terr.Header.VertexStreamDescription.Dimensions[0].X * terr.Header.VertexStreamDescription.Dimensions[0].Y;
            var gridWidth = (int)terr.Header.VertexStreamDescription.Dimensions[0].X;
            var gridHeight = (int)terr.Header.VertexStreamDescription.Dimensions[0].Y;
            int gridSize = (int)terr.Header.VertexStreamDescription.TileDimensions;

            for (int i = 0; i < fieldDimensions; i++)
            {
                int column = i % gridWidth;
                int row = i / gridWidth;

                string baseFolder = Path.Combine(Path.GetFileNameWithoutExtension(pak.FileInfo.FilePath), folder);
                string currentFolder = "";

                if (row < 17)
                {
                    currentFolder = Path.Combine(baseFolder, "Group_1_1");
                    if (column > 16)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_1_2");
                    }
                    if (column > 33)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_1_3");
                    }
                    if (column > 50)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_1_4");
                    }
                    if (column > 67)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_1_5");
                    }
                }
                if (row > 16)
                {
                    currentFolder = Path.Combine(baseFolder, "Group_2_1");
                    if (column > 16)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_2_2");
                    }
                    if (column > 33)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_2_3");
                    }
                    if (column > 50)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_2_4");
                    }
                    if (column > 67)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_2_5");
                    }
                }
                if (row > 33)
                {
                    currentFolder = Path.Combine(baseFolder, "Group_3_1");
                    if (column > 16)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_3_2");
                    }
                    if (column > 33)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_3_3");
                    }
                    if (column > 50)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_3_4");
                    }
                    if (column > 67)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_3_5");
                    }
                }
                if (row > 50)
                {
                    currentFolder = Path.Combine(baseFolder, "Group_4_1");
                    if (column > 16)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_4_2");
                    }
                    if (column > 33)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_4_3");
                    }
                    if (column > 50)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_4_4");
                    }
                    if (column > 67)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_4_5");
                    }
                }
                if (row > 67)
                {
                    currentFolder = Path.Combine(baseFolder, "Group_5_1");
                    if (column > 16)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_5_2");
                    }
                    if (column > 33)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_5_3");
                    }
                    if (column > 50)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_5_4");
                    }
                    if (column > 67)
                    {
                        currentFolder = Path.Combine(baseFolder, "Group_5_5");
                    }
                }

                if (!Directory.Exists(currentFolder))
                {
                    Directory.CreateDirectory(currentFolder);
                }

                string path = Path.Combine(currentFolder, $"{row}_{column}.obj");

                // Determine if we have neighbors to bridge gaps with
                bool hasRight = column < gridWidth - 1;
                bool hasBottom = row < gridHeight - 1;

                // Extend the vertex grid by 1 if there is a neighbor
                int vertsX = hasRight ? gridSize + 1 : gridSize;
                int vertsZ = hasBottom ? gridSize + 1 : gridSize;

                // Bounding box check to only export the populated center tiles
                if (column > 20 && column < 66 && row > 20 && row < 66)
                {
                    
                }

                // 1. Initialize the Scene and Model INSIDE the loop for this specific tile
                IOScene ioscene = new IOScene();
                IOModel iomodel = new IOModel();

                string tileName = $"Tile_{row}_{column}";
                iomodel.Name = tileName;
                ioscene.Models.Add(iomodel);

                IOMesh iomesh = new IOMesh();
                iomesh.Name = $"{tileName}_Mesh";

                // 2. Write vertices (including borrowed neighbor edges)
                for (int z = 0; z < vertsZ; z++)
                {
                    for (int x = 0; x < vertsX; x++)
                    {
                        int srcRow = row;
                        int srcCol = column;
                        int srcX = x;
                        int srcZ = z;

                        float offsetX = 0f;
                        float offsetZ = 0f;

                        // If we hit the extended right edge, grab from the Right neighbor
                        if (x == gridSize)
                        {
                            srcCol = column + 1;
                            srcX = 0;
                            offsetX = gridSize;
                        }

                        // If we hit the extended bottom edge, grab from the Bottom neighbor
                        if (z == gridSize)
                        {
                            srcRow = row + 1;
                            srcZ = 0;
                            offsetZ = -gridSize;
                        }

                        TerrainTile srcTile = terr.tiles[srcRow * gridWidth + srcCol];
                        var vert = srcTile.tileVerts[(srcZ * gridSize) + srcX];

                        float finalX = vert.Position.X + offsetX;
                        float finalY = vert.Position.Y;
                        float finalZ = vert.Position.Z + offsetZ;

                        var iovertex = new IOVertex()
                        {
                            Position = new System.Numerics.Vector3(finalX, finalY, finalZ),
                        };

                        iomesh.Vertices.Add(iovertex);

                        byte texSelect = vert.textureUsage;
                        float normalizedByte = 0;


                        if (texSelect == 1)
                            normalizedByte = 0.1f;
                        if (texSelect == 2)
                            normalizedByte = 0.2f;
                        if (texSelect == 3)
                            normalizedByte = 0.3f;
                        if (texSelect == 4)
                            normalizedByte = 0.4f;
                        if (texSelect == 5)
                            normalizedByte = 0.5f;
                        if (texSelect == 6)
                            normalizedByte = 0.6f;
                        if (texSelect == 7)
                            normalizedByte = 0.7f;
                        if (texSelect == 8)
                            normalizedByte = 0.8f;
                        if (texSelect == 9)
                            normalizedByte = 0.9f;


                        // Store in Red channel. (R, G, B, A, colorIndex)
                        iovertex.SetColor(normalizedByte, 0.0f, 0.0f, 1.0f, 0);

                    }
                }

                // 3. Create Polygon (Faces)
                IOPolygon iopoly = new IOPolygon();
                iopoly.MaterialName = "Terrain_Default";

                for (int z = 0; z < vertsZ - 1; z++)
                {
                    for (int x = 0; x < vertsX - 1; x++)
                    {
                        int topLeft = (z * vertsX + x);
                        int topRight = topLeft + 1;
                        int bottomLeft = ((z + 1) * vertsX + x);
                        int bottomRight = bottomLeft + 1;

                        // Triangle 1
                        iopoly.Indicies.Add(topLeft);
                        iopoly.Indicies.Add(bottomLeft);
                        iopoly.Indicies.Add(topRight);

                        // Triangle 2
                        iopoly.Indicies.Add(topRight);
                        iopoly.Indicies.Add(bottomLeft);
                        iopoly.Indicies.Add(bottomRight);
                    }
                }

                // Bind polygon to mesh, and mesh to model
                iomesh.Polygons.Add(iopoly);
                iomodel.Meshes.Add(iomesh);

                // 4. Export THIS specific tile directly to disk before moving to the next one
                string tilePath = Path.Combine(currentFolder, $"{tileName}.gltf");
                IOManager.ExportScene(ioscene, tilePath, new ExportSettings()
                {
                    Optimize = true,
                });
            }
        }

        public static void ExportMergedTerrainOBJ(TERR terr, string exportFolder, string fileName = "MergedTerrain.obj")
        {
            var gridWidth = (int)terr.Header.VertexStreamDescription.Dimensions[0].X;
            var gridHeight = (int)terr.Header.VertexStreamDescription.Dimensions[0].Y;
            int gridSize = (int)terr.Header.VertexStreamDescription.TileDimensions;
            int fieldDimensions = gridWidth * gridHeight;

            if (!Directory.Exists(exportFolder))
                Directory.CreateDirectory(exportFolder);

            string savePath = Path.Combine(exportFolder, fileName);

            using (StreamWriter writer = new StreamWriter(savePath))
            {
                writer.WriteLine($"o Full_Stitched_Terrain");

                // 1. Write ALL vertices across the entire grid
                for (int i = 0; i < fieldDimensions; i++)
                {
                    int col = i % gridWidth;
                    int row = i / gridWidth;
                    TerrainTile tile = terr.tiles[i];

                    // Offset the tile in world space
                    // Using gridSize (e.g., 96) keeps the exact 1-unit physical gap 
                    // needed for the bridging faces to be perfectly square.
                    float offsetX = col * gridSize;
                    float offsetZ = row * gridSize;

                    foreach (var vert in tile.tileVerts)
                    {
                        writer.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "v {0:F6} {1:F6} {2:F6}",
                            vert.Position.X + offsetX, vert.Position.Y, vert.Position.Z + offsetZ));
                    }
                }

                // Helper local function to calculate the absolute OBJ vertex index (1-based)
                int GetVertexIndex(int row, int col, int localX, int localZ)
                {
                    int tileIndex = (row * gridWidth) + col;
                    int vertexOffset = tileIndex * (gridSize * gridSize);
                    int localIndex = (localZ * gridSize) + localX;
                    return vertexOffset + localIndex + 1;
                }

                // 2. Write Internal Faces for each tile
                for (int row = 0; row < gridHeight; row++)
                {
                    for (int col = 0; col < gridWidth; col++)
                    {
                        for (int z = 0; z < gridSize - 1; z++)
                        {
                            for (int x = 0; x < gridSize - 1; x++)
                            {
                                int tl = GetVertexIndex(row, col, x, z);
                                int tr = GetVertexIndex(row, col, x + 1, z);
                                int bl = GetVertexIndex(row, col, x, z + 1);
                                int br = GetVertexIndex(row, col, x + 1, z + 1);

                                writer.WriteLine($"f {tl} {bl} {tr}");
                                writer.WriteLine($"f {tr} {bl} {br}");
                            }
                        }
                    }
                }

                // 3. Write Stitching Faces: East-West (Between Columns)
                for (int row = 0; row < gridHeight; row++)
                {
                    for (int col = 0; col < gridWidth - 1; col++)
                    {
                        for (int z = 0; z < gridSize - 1; z++)
                        {
                            int tl = GetVertexIndex(row, col, gridSize - 1, z);
                            int tr = GetVertexIndex(row, col + 1, 0, z);
                            int bl = GetVertexIndex(row, col, gridSize - 1, z + 1);
                            int br = GetVertexIndex(row, col + 1, 0, z + 1);

                            writer.WriteLine($"f {tl} {bl} {tr}");
                            writer.WriteLine($"f {tr} {bl} {br}");
                        }
                    }
                }

                // 4. Write Stitching Faces: North-South (Between Rows)
                for (int row = 0; row < gridHeight - 1; row++)
                {
                    for (int col = 0; col < gridWidth; col++)
                    {
                        for (int x = 0; x < gridSize - 1; x++)
                        {
                            int tl = GetVertexIndex(row, col, x, gridSize - 1);
                            int tr = GetVertexIndex(row, col, x + 1, gridSize - 1);
                            int bl = GetVertexIndex(row + 1, col, x, 0);
                            int br = GetVertexIndex(row + 1, col, x + 1, 0);

                            writer.WriteLine($"f {tl} {bl} {tr}");
                            writer.WriteLine($"f {tr} {bl} {br}");
                        }
                    }
                }

                // 5. Write Stitching Faces: Corners (Intersections of 4 tiles)
                for (int row = 0; row < gridHeight - 1; row++)
                {
                    for (int col = 0; col < gridWidth - 1; col++)
                    {
                        int tl = GetVertexIndex(row, col, gridSize - 1, gridSize - 1);
                        int tr = GetVertexIndex(row, col + 1, 0, gridSize - 1);
                        int bl = GetVertexIndex(row + 1, col, gridSize - 1, 0);
                        int br = GetVertexIndex(row + 1, col + 1, 0, 0);

                        writer.WriteLine($"f {tl} {bl} {tr}");
                        writer.WriteLine($"f {tr} {bl} {br}");
                    }
                }
            }
            Console.WriteLine($"[OBJ Export] Successfully merged and stitched terrain into: {savePath}");
        }

        public static void ExportHeightmapPNG(TERR terr, string folder, PAK pak)
        {
            var gridWidth = (int)terr.Header.VertexStreamDescription.Dimensions[0].X;
            var gridHeight = (int)terr.Header.VertexStreamDescription.Dimensions[0].Y;
            int gridSize = (int)terr.Header.VertexStreamDescription.TileDimensions;
            int fieldDimensions = gridWidth * gridHeight;

            float minHeight = float.MaxValue;
            float maxHeight = float.MinValue;

            // 1. Find Global Min and Max Height
            for (int i = 0; i < fieldDimensions; i++)
            {
                foreach (var vert in terr.tiles[i].tileVerts)
                {
                    if (vert.Position.Y < minHeight) minHeight = vert.Position.Y;
                    if (vert.Position.Y > maxHeight) maxHeight = vert.Position.Y;
                }
            }

            float heightRange = maxHeight - minHeight;
            if (heightRange == 0) heightRange = 1f;

            int imgWidth = gridWidth * gridSize;
            int imgHeight = gridHeight * gridSize;

            // 2. Create a 16-bit Grayscale image using ImageSharp
            using (Image<L16> img = new Image<L16>(imgWidth, imgHeight))
            {
                for (int i = 0; i < fieldDimensions; i++)
                {
                    int tileCol = i % gridWidth;
                    int tileRow = i / gridWidth;
                    TerrainTile tile = terr.tiles[i];

                    for (int z = 0; z < gridSize; z++)
                    {
                        for (int x = 0; x < gridSize; x++)
                        {
                            int vertIndex = (z * gridSize) + x;
                            float currentHeight = tile.tileVerts[vertIndex].Position.Y;

                            // Normalize the height between 0.0 and 1.0
                            float normalized = (currentHeight - minHeight) / heightRange;

                            // Map to a 16-bit integer (0 to 65,535)
                            ushort colorVal = (ushort)Math.Clamp(normalized * 65535.0f, 0f, 65535f);

                            // Calculate absolute pixel coordinates
                            int pixelX = (tileCol * gridSize) + x;
                            int pixelY = (tileRow * gridSize) + z;

                            // Invert the Y axis to fix vertical mirroring
                            int flippedY = (imgHeight - 1) - pixelY;

                            // Set the pixel
                            img[pixelX, flippedY] = new L16(colorVal);
                        }
                    }
                }

                string currentFolder = Path.Combine(Path.GetFileNameWithoutExtension(pak.FileInfo.FilePath), folder);
                if (!Directory.Exists(currentFolder))
                    Directory.CreateDirectory(currentFolder);

                string savePath = Path.Combine(currentFolder, "TerrHeightMap.png");
                img.SaveAsPng(savePath);

                Console.WriteLine($"[Heightmap Exported] 16-bit PNG saved to: {savePath}");
            }
        }

        public static void ExportTextureSelectMap(TERR terr, string folder,PAK pak)
        {
            var gridWidth = (int)terr.Header.VertexStreamDescription.Dimensions[0].X;
            var gridHeight = (int)terr.Header.VertexStreamDescription.Dimensions[0].Y;
            int gridSize = (int)terr.Header.VertexStreamDescription.TileDimensions;
            int fieldDimensions = gridWidth * gridHeight;

            float minHeight = float.MaxValue;
            float maxHeight = float.MinValue;

            // 1. Find Global Min and Max Height
            for (int i = 0; i < fieldDimensions; i++)
            {
                foreach (var vert in terr.tiles[i].tileVerts)
                {
                    if (vert.Position.Y < minHeight) minHeight = vert.Position.Y;
                    if (vert.Position.Y > maxHeight) maxHeight = vert.Position.Y;
                }
            }

            float heightRange = maxHeight - minHeight;
            if (heightRange == 0) heightRange = 1f;

            int imgWidth = gridWidth * gridSize;
            int imgHeight = gridHeight * gridSize;

            // 2. Create a 16-bit Grayscale image using ImageSharp
            using (Image<L16> img = new Image<L16>(imgWidth, imgHeight))
            {
                for (int i = 0; i < fieldDimensions; i++)
                {
                    int tileCol = i % gridWidth;
                    int tileRow = i / gridWidth;
                    TerrainTile tile = terr.tiles[i];

                    for (int z = 0; z < gridSize; z++)
                    {
                        for (int x = 0; x < gridSize; x++)
                        {
                            int vertIndex = (z * gridSize) + x;
                            float currentHeight = tile.tileVerts[vertIndex].Position.Y;

                            // Normalize the height between 0.0 and 1.0
                            float normalized = (currentHeight - minHeight) / heightRange;

                            // Map to a 16-bit integer (0 to 65,535)


                            ushort colorVal = tile.textureSelect[vertIndex];

                            colorVal = (ushort)(0 + (colorVal - 0) * (65535 - 0) / (10 - 0));


                            // Calculate absolute pixel coordinates
                            int pixelX = (tileCol * gridSize) + x;
                            int pixelY = (tileRow * gridSize) + z;

                            // Invert the Y axis to fix vertical mirroring
                            int flippedY = (imgHeight - 1) - pixelY;

                            // Set the pixel
                            img[pixelX, flippedY] = new L16(colorVal);
                        }
                    }
                }

                string currentFolder = Path.Combine(Path.GetFileNameWithoutExtension(pak.FileInfo.FilePath), folder);
                if (!Directory.Exists(currentFolder))
                    Directory.CreateDirectory(currentFolder);

                string savePath = Path.Combine(currentFolder, "TerrTextureSelectMap.png");
                img.SaveAsPng(savePath);

                Console.WriteLine($"[Texture Select Map Exported] 16-bit PNG saved to: {savePath}");
            }
        }

        public static void ExportUnknownBitMap(TERR terr, string folder, PAK pak)
        {
            var gridWidth = (int)terr.Header.VertexStreamDescription.Dimensions[0].X;
            var gridHeight = (int)terr.Header.VertexStreamDescription.Dimensions[0].Y;
            int gridSize = (int)terr.Header.VertexStreamDescription.TileDimensions;
            int fieldDimensions = gridWidth * gridHeight;

            float minHeight = float.MaxValue;
            float maxHeight = float.MinValue;

            // 1. Find Global Min and Max Height
            for (int i = 0; i < fieldDimensions; i++)
            {
                foreach (var vert in terr.tiles[i].tileVerts)
                {
                    if (vert.Position.Y < minHeight) minHeight = vert.Position.Y;
                    if (vert.Position.Y > maxHeight) maxHeight = vert.Position.Y;
                }
            }

            float heightRange = maxHeight - minHeight;
            if (heightRange == 0) heightRange = 1f;

            int imgWidth = gridWidth * gridSize;
            int imgHeight = gridHeight * gridSize;

            // 2. Create a 16-bit Grayscale image using ImageSharp
            using (Image<L16> img = new Image<L16>(imgWidth, imgHeight))
            {
                for (int i = 0; i < fieldDimensions; i++)
                {
                    int tileCol = i % gridWidth;
                    int tileRow = i / gridWidth;
                    TerrainTile tile = terr.tiles[i];

                    for (int z = 0; z < gridSize; z++)
                    {
                        for (int x = 0; x < gridSize; x++)
                        {
                            int vertIndex = (z * gridSize) + x;
                            float currentHeight = tile.tileVerts[vertIndex].Position.Y;

                            // Normalize the height between 0.0 and 1.0
                            float normalized = (currentHeight - minHeight) / heightRange;

                            // Map to a 16-bit integer (0 to 65,535)
                            ushort colorVal = tile.unk1[vertIndex];

                            colorVal = (ushort)(0 + (colorVal - 0) * (65535 - 0) / (255 - 0));

                            // Calculate absolute pixel coordinates
                            int pixelX = (tileCol * gridSize) + x;
                            int pixelY = (tileRow * gridSize) + z;

                            // Invert the Y axis to fix vertical mirroring
                            int flippedY = (imgHeight - 1) - pixelY;

                            // Set the pixel
                            img[pixelX, flippedY] = new L16(colorVal);
                        }
                    }
                }

                string currentFolder = Path.Combine(Path.GetFileNameWithoutExtension(pak.FileInfo.FilePath), folder);
                if (!Directory.Exists(currentFolder))
                    Directory.CreateDirectory(currentFolder);

                string savePath = Path.Combine(currentFolder, "TerrUnknownByteMap.png");
                img.SaveAsPng(savePath);

                Console.WriteLine($"[Unknown Byte Map Exported] 16-bit PNG saved to: {savePath}");
            }
        }
    }


    public static class MathExtensions
    {
        public static double Map(this double value, double fromLow, double fromHigh, double toLow, double toHigh)
        {
            // Avoid division by zero if fromLow equals fromHigh
            if (fromLow == fromHigh) return toLow;

            return toLow + (value - fromLow) * (toHigh - toLow) / (fromHigh - fromLow);
        }
    }
}