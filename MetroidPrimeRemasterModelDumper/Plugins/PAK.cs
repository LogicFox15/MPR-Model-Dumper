using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.IO;
using AvaloniaToolbox.Core;
using AvaloniaToolbox.Core.IO;
using ImageLibrary.Utils;

namespace DKCTF
{
    public class PAK : IArchiveFile, IFileFormat
    {
        public bool CanSave { get; set; }
        public string[] Description { get; set; } = new string[] { "DKCTF Archive" };
        public string[] Extension { get; set; } = new string[] { "*.pak" };
        public FileContext FileInfo { get; set; }

        public bool CanAddFiles { get; set; }
        public bool CanRenameFiles { get; set; }
        public bool CanReplaceFiles { get; set; }
        public bool CanDeleteFiles { get; set; }

        public Task<bool> Identify(FileContext ctx)
        {
            using (var reader = new FileReader(ctx.Stream, true))
            {
                bool IsForm = reader.CheckSignature("RFRM");
                bool FormType = reader.CheckSignature("PACK", 20);

                return Task.FromResult(IsForm && FormType);
            }
        }

        public List<FileEntry> files = new List<FileEntry>();
        public IEnumerable<ArchiveFileInfo> Files => files;

        public void ClearFiles() { files.Clear(); }

        //For file searching
        public Dictionary<string, FileEntry> ModelFiles = new Dictionary<string, FileEntry>();
        public Dictionary<string, FileEntry> SkeletonFiles = new Dictionary<string, FileEntry>();
        public Dictionary<string, FileEntry> TextureFiles = new Dictionary<string, FileEntry>();
        public Dictionary<string, CHAR> CharFiles = new Dictionary<string, CHAR>();
        public Dictionary<string, FileEntry> AnimFiles = new Dictionary<string, FileEntry>();

        public PACK PakData;

        internal bool IsMPR;

        public Task Load(FileContext ctx)
        {
            this.FileInfo.KeepOpen = true;

            PACK pack = new PACK(ctx.Stream);
            PakData = pack;

            for (int i = 0; i < pack.Assets.Count; i++)
            {
                string ext = pack.Assets[i].Type.ToLower();

                FileEntry file = new FileEntry();
                file.ParentArchive = this;
                file.ArchiveStream = ctx.Stream;
                file.AssetEntry = pack.Assets[i];

                string dir = pack.Assets[i].Type;
                if (DirectoryLabels.ContainsKey(dir))
                    dir = DirectoryLabels[dir];

                file.FileName = $"{dir}/{pack.Assets[i].FileID}.{ext}";
                file.SubData = new SubStream(ctx.Stream, pack.Assets[i].Offset, pack.Assets[i].Size);

                if (pack.MetaOffsets.ContainsKey(pack.Assets[i].FileID.ToString()))
                    file.MetaPointer = pack.MetaDataOffset + pack.MetaOffsets[pack.Assets[i].FileID.ToString()];
                files.Add(file);

                

                try
                {
                    switch (file.AssetEntry.Type)
                    {
                        case "SMDL": ModelFiles.Add(file.AssetEntry.FileID.ToString(), file); break;
                        case "TXTR": TextureFiles.Add(file.AssetEntry.FileID.ToString(), file); break;
                        case "SKEL": SkeletonFiles.Add(file.AssetEntry.FileID.ToString(), file); break;
                        case "ANIM": AnimFiles.Add(file.AssetEntry.FileID.ToString(), file); break;
                    }
                }
                catch
                {
                    continue;
                }

                /*
                try
                {
                    
                }
                catch
                {
                    continue;
                }
                */
                
            }

            foreach (var c in CharFiles)
            {
                if (SkeletonFiles.ContainsKey(c.Value.SkeletonFileID.ToString()))
                    SkeletonFiles[c.Value.SkeletonFileID.ToString()].FileName = $"Characters/{c.Value.Name}/Models/{c.Value.SkeletonFileID}.skel";

                foreach (var m in c.Value.Models)
                {
                    if (ModelFiles.ContainsKey(m.FileID.ToString()))
                        ModelFiles[m.FileID.ToString()].FileName = $"Characters/{c.Value.Name}/Models/{m.Name}.smdl";
                }
                foreach (var m in c.Value.Animations)
                {
                    if (AnimFiles.ContainsKey(m.FileID.ToString()))
                        AnimFiles[m.FileID.ToString()].FileName = $"Characters/{c.Value.Name}/Animations/{m.Name}.anim";
                }
            }
            return Task.CompletedTask;
        }

        Dictionary<string, string> DirectoryLabels = new Dictionary<string, string>()
        {
            { "CHAR", "Characters" },
            { "CHPR", "Character Project" },
            { "CMDL", "Static Models" },
            { "SMDL", "Skinned Models" },
            { "TXTR", "Textures" },
            { "MTRL", "Shaders" },
            { "CSMP", "AudioSample" },
            { "CAUD", "AudioData" },
            { "GENP", "Gpsys" },
            { "ANIM", "Animations" },
            { "XFRM", "Xfpsys" },
            { "WMDL", "World Models" },
            { "DCLN", "Collision Models" },
            { "CLSN", "Collision Static Models" },
        };

        public Task Save(FileContext ctx)
        {
            return Task.CompletedTask;
        }

        public bool AddFile(ArchiveFileInfo archiveFileInfo)
        {
            return false;
        }

        public bool DeleteFile(ArchiveFileInfo archiveFileInfo)
        {
            return false;
        }
    }

    public class FileEntry : ArchiveFileInfo
    {
        public PACK.DirectoryAssetEntry AssetEntry;

        public PAK ParentArchive;

        public long MetaPointer;

        public Stream SubData;

        public Stream ArchiveStream;

        public override Stream FileData 
        {
            get
            {
                List<byte[]> Data = new List<byte[]>();

                using (var reader = new FileReader(SubData, true))
                {
                    var data = reader.ReadBytes((int)reader.BaseStream.Length);

                    reader.Position = 0;
                    if (AssetEntry.DecompressedSize != AssetEntry.Size)
                        data = IOFileExtension.DecompressedBuffer(reader, (uint)AssetEntry.Size, (uint)AssetEntry.DecompressedSize, true);

                    Data.Add(data);

                    if (WriteMetaData)
                    {
                        using (var r = new FileReader(ArchiveStream, true)) {
                            r.SetByteOrder(!ParentArchive.PakData.IsLittleEndian);

                            Data.Add(FileForm.WriteMetaFooter(r, (uint)MetaPointer, AssetEntry.Type, ParentArchive.PakData));
                        }
                    }
                }

                if (AssetEntry.Type == "TXTR")
                {
                    var txt = new TXTR();
                     return new MemoryStream(txt.CreateUncompressedFile(ByteUtil.CombineByteArray(Data.ToArray()),
                         ParentArchive.PakData.FileHeader, ParentArchive.PakData.IsMPR));
                }


                return new MemoryStream(ByteUtil.CombineByteArray(Data.ToArray()));
            }
        }

        public bool WriteMetaData
        {
            get
            {
                switch (AssetEntry.Type)
                {
                    case "CMDL":
                    case "SMDL":
                    case "WMDL":
                    case "TXTR":
                        return true;
                    default:
                        return false;
                }
            }
        }
/*
        public override IFileFormat OpenFile()
        {
            var pak = this.ParentArchive;

            var file = base.OpenFile();
            if (file is CModel)
            {
                ((CModel)file).LoadTextures(pak.TextureFiles);

                FileEntry GetSkeleton()
                {
                    foreach (var c in pak.CharFiles)
                    {
                        foreach (var m in c.Value.Models)
                        {
                            if (AssetEntry.FileID.ToString() == m.FileID.ToString())
                                return pak.SkeletonFiles[c.Value.SkeletonFileID.ToString()];
                        }
                    }
                    return null;
                }
                var skelFile = GetSkeleton();
                if (skelFile != null)
                {
                    var skel = new SKEL(new MemoryStream(skelFile.FileData));
                    ((CModel)file).LoadSkeleton(skel);
                }
            }
            if (file is CCharacter)
                ((CCharacter)file).LoadModels(pak);
            
            this.FileFormat = file;

            return file;
        }*/
    }
}
