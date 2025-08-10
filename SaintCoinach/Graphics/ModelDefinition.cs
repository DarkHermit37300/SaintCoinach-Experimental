using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SaintCoinach.Graphics {
    public class ModelDefinition {
        public const int StringsCountOffset = 0x00;
        public const int StringsSizeOffset = 0x04;
        public const int StringsOffset = 0x08;

        public const int ModelCount = 3;

        #region Fields
        internal string[] MaterialNames;
        internal string[] AttributeNames;
        private Model[] _Models = new Model[ModelCount];
        #endregion

        #region Properties
        public IEnumerable<ModelQuality> AvailableQualities { get; private set; }
        public ModelDefinitionHeader Header { get; private set; }
        public ModelFile File { get; private set; }
        public VertexFormat[] VertexFormats { get; private set; }
        public Unknowns.ModelStruct1[] UnknownStructs1 { get; private set; }
        public ModelHeader[] ModelHeaders { get; private set; }
        public MeshHeader[] MeshHeaders { get; private set; }
        public ModelAttribute[] Attributes { get; private set; }
        public Unknowns.ModelStruct2[] UnknownStructs2 { get; private set; }
        public MeshPartHeader[] MeshPartHeaders { get; private set; }
        public Unknowns.ModelStruct3[] UnknownStructs3 { get; private set; }
        public MaterialDefinition[] Materials { get; private set; }
        public string[] BoneNames { get; private set; }
        public Unknowns.BoneList[] BoneLists { get; private set; }
        public Unknowns.ModelStruct5[] UnknownStructs5 { get; private set; }
        public Unknowns.ModelStruct6[] UnknownStructs6 { get; private set; }
        public Unknowns.ModelStruct7[] UnknownStructs7 { get; private set; }
        public Unknowns.BoneIndices BoneIndices { get; private set; }
        public ModelBoundingBoxes BoundingBoxes { get; private set; }
        public Bone[] Bones { get; private set; }
        #endregion

        #region Constructor
        public ModelDefinition(ModelFile file) {
            File = file;
            Build();
        }
        #endregion

        #region Get
        public Model GetModel(int quality) { return GetModel((ModelQuality)quality); }
        public Model GetModel(ModelQuality quality) {
            var v = (int)quality;
            if (v < 0 || v >= _Models.Length)
                throw new ArgumentOutOfRangeException(nameof(quality), $"Quality {quality} is out of range");
            
            if (_Models[v] == null)
                _Models[v] = new Model(this, quality);
            return _Models[v];
        }
        #endregion

        #region Build
        private void Build() {
            const int DefinitionPart = 1;

            try {
                var buffer = File.GetPart(DefinitionPart);
                
                // Find where the strings section begins
                var stringsInfo = FindStringsSection(buffer);
                var stringsOffset = stringsInfo.StringsOffset;
                var stringsSize = stringsInfo.StringsSize;
                var headerOffset = stringsOffset + stringsSize;

                // osg and bil_*_base.mdl workaround
                bool isOsg = File.Path.Contains("/osg_");

                var offset = headerOffset;
                
                // Validate we have enough space for the header
                if (offset + System.Runtime.InteropServices.Marshal.SizeOf<ModelDefinitionHeader>() > buffer.Length) {
                    throw new InvalidOperationException($"Buffer too small for header at offset {offset:X}");
                }

                this.Header = buffer.ToStructure<ModelDefinitionHeader>(ref offset);
                
                // Add debug output to see what we're reading
                System.Diagnostics.Debug.WriteLine($"Header read from offset {headerOffset:X}:");
                System.Diagnostics.Debug.WriteLine($"  MeshCount: {Header.MeshCount}");
                System.Diagnostics.Debug.WriteLine($"  AttributeCount: {Header.AttributeCount}");
                System.Diagnostics.Debug.WriteLine($"  PartCount: {Header.PartCount}");
                System.Diagnostics.Debug.WriteLine($"  MaterialCount: {Header.MaterialCount}");
                System.Diagnostics.Debug.WriteLine($"  BoneCount: {Header.BoneCount}");
                
                ValidateHeaderCounts();

                this.UnknownStructs1 = SafeToStructures<Unknowns.ModelStruct1>(buffer, Header.UnknownStruct1Count, ref offset);
                this.ModelHeaders = SafeToStructures<ModelHeader>(buffer, ModelCount, ref offset);

                // Skip 120 bytes after model headers for OSG models
                if (isOsg && offset + 120 <= buffer.Length) {
                    offset += 120;
                }

                var availableQualities = new List<ModelQuality>();
                for (var i = 0; i < Math.Min(this.ModelHeaders.Length, 3); ++i) {
                    if (this.ModelHeaders[i].MeshCount > 0)
                        availableQualities.Add((ModelQuality)i);
                }
                this.AvailableQualities = availableQualities;

                this.MeshHeaders = SafeToStructures<MeshHeader>(buffer, Header.MeshCount, ref offset);

                this.AttributeNames = ReadStrings(buffer, Header.AttributeCount, ref offset, stringsOffset);
                if (Header.AttributeCount > 0) {
                    this.Attributes = new ModelAttribute[Header.AttributeCount];
                    for (var i = 0; i < Header.AttributeCount; ++i)
                        this.Attributes[i] = new ModelAttribute(this, i);
                }

                this.UnknownStructs2 = SafeToStructures<Unknowns.ModelStruct2>(buffer, Header.UnknownStruct2Count, ref offset);
                this.MeshPartHeaders = SafeToStructures<MeshPartHeader>(buffer, Header.PartCount, ref offset);
                this.UnknownStructs3 = SafeToStructures<Unknowns.ModelStruct3>(buffer, Header.UnknownStruct3Count, ref offset);

                this.MaterialNames = ReadStrings(buffer, Header.MaterialCount, ref offset, stringsOffset);
                if (Header.MaterialCount > 0) {
                    this.Materials = new MaterialDefinition[Header.MaterialCount];
                    for (var i = 0; i < Header.MaterialCount; ++i)
                        this.Materials[i] = new MaterialDefinition(this, i);
                }

                this.BoneNames = ReadStrings(buffer, Header.BoneCount, ref offset, stringsOffset);
                this.BoneLists = SafeToStructures<Unknowns.BoneList>(buffer, Header.UnknownStruct4Count, ref offset);
                this.UnknownStructs5 = SafeToStructures<Unknowns.ModelStruct5>(buffer, Header.UnknownStruct5Count, ref offset);
                this.UnknownStructs6 = SafeToStructures<Unknowns.ModelStruct6>(buffer, Header.UnknownStruct6Count, ref offset);
                this.UnknownStructs7 = SafeToStructures<Unknowns.ModelStruct7>(buffer, Header.UnknownStruct7Count, ref offset);

                if (offset < buffer.Length) {
                    this.BoneIndices = new Unknowns.BoneIndices(buffer, ref offset);
                }

                // Handle padding
                if (offset < buffer.Length && buffer[offset] <= 32) {
                    var paddingSize = buffer[offset];
                    if (offset + paddingSize + 1 <= buffer.Length) {
                        offset += paddingSize + 1;
                    }
                }

                if (offset + System.Runtime.InteropServices.Marshal.SizeOf<ModelBoundingBoxes>() <= buffer.Length) {
                    this.BoundingBoxes = buffer.ToStructure<ModelBoundingBoxes>(ref offset);
                }

                if (Header.BoneCount > 0) {
                    this.Bones = new Bone[Header.BoneCount];
                    for (var i = 0; i < Header.BoneCount; ++i) {
                        if (offset < buffer.Length) {
                            this.Bones[i] = new Bone(this, i, buffer, ref offset);
                        }
                    }
                }

                BuildVertexFormats();
                
            } catch (Exception ex) {
                throw new InvalidOperationException($"Failed to parse model definition for {File.Path}: {ex.Message}", ex);
            }
        }

        private struct StringsInfo {
            public int StringsOffset;
            public int StringsSize;
            public int StringsCount;
        }

        private StringsInfo FindStringsSection(byte[] buffer) {
            // Search for the strings section by looking for the pattern:
            // [strings_count] [strings_size] [string_data...]
            
            for (int i = 0; i < buffer.Length - 8; i += 4) {
                var potentialCount = BitConverter.ToInt32(buffer, i);
                var potentialSize = BitConverter.ToInt32(buffer, i + 4);
                
                // Reasonable bounds for strings count and size
                if (potentialCount > 0 && potentialCount < 1000 && 
                    potentialSize > 0 && potentialSize < buffer.Length &&
                    i + 8 + potentialSize <= buffer.Length) {
                    
                    // Check if this looks like actual string data
                    if (LooksLikeStrings(buffer, i + 8, potentialSize)) {
                        return new StringsInfo {
                            StringsOffset = i + 8,
                            StringsSize = potentialSize,
                            StringsCount = potentialCount
                        };
                    }
                }
            }
            
            throw new InvalidOperationException("Could not find strings section in model file");
        }

        private bool LooksLikeStrings(byte[] buffer, int offset, int size) {
            if (offset + size > buffer.Length) return false;
            
            int nullTerminators = 0;
            int printableChars = 0;
            int totalChars = 0;
            
            for (int i = offset; i < offset + Math.Min(size, 200); i++) {
                totalChars++;
                if (buffer[i] == 0) {
                    nullTerminators++;
                } else if (buffer[i] >= 32 && buffer[i] < 127) {
                    printableChars++;
                }
            }
            
            // Should have a good ratio of printable characters and some null terminators
            return nullTerminators >= 3 && 
                   printableChars > totalChars * 0.6 && 
                   (printableChars + nullTerminators) > totalChars * 0.8;
        }

        private T[] SafeToStructures<T>(byte[] buffer, int count, ref int offset) where T : struct {
            if (count <= 0 || count > 10000) return new T[0];
            
            var structSize = System.Runtime.InteropServices.Marshal.SizeOf<T>();
            if (offset + (structSize * count) > buffer.Length) {
                throw new InvalidOperationException($"Buffer too small for {count} structures of type {typeof(T).Name} at offset {offset:X}");
            }
            
            return buffer.ToStructures<T>(count, ref offset);
        }

        private void ValidateHeaderCounts() {
            const int MAX_COUNT = 10000;
            
            if (Header.MeshCount < 0 || Header.MeshCount > MAX_COUNT)
                throw new InvalidOperationException($"Invalid MeshCount: {Header.MeshCount}");
            if (Header.AttributeCount < 0 || Header.AttributeCount > MAX_COUNT)
                throw new InvalidOperationException($"Invalid AttributeCount: {Header.AttributeCount}");
            if (Header.PartCount < 0 || Header.PartCount > MAX_COUNT)
                throw new InvalidOperationException($"Invalid PartCount: {Header.PartCount}");
            if (Header.MaterialCount < 0 || Header.MaterialCount > MAX_COUNT)
                throw new InvalidOperationException($"Invalid MaterialCount: {Header.MaterialCount}");
            if (Header.BoneCount < 0 || Header.BoneCount > MAX_COUNT)
                throw new InvalidOperationException($"Invalid BoneCount: {Header.BoneCount}");
        }

        private void BuildVertexFormats() {
            const int FormatPart = 0;

            try {
                var buffer = File.GetPart(FormatPart);

                if (Header.MeshCount > 0) {
                    this.VertexFormats = new VertexFormat[Header.MeshCount];
                    var offset = 0;
                    for (var i = 0; i < Header.MeshCount; ++i) {
                        if (offset < buffer.Length) {
                            this.VertexFormats[i] = new VertexFormat(buffer, ref offset);
                        }
                    }
                }
            } catch (Exception ex) {
                throw new InvalidOperationException($"Failed to build vertex formats: {ex.Message}", ex);
            }
        }

        private static string[] ReadStrings(byte[] buffer, int count, ref int offset, int stringsBaseOffset) {
            if (count <= 0) return new string[0];
            
            var values = new string[count];
            for (var i = 0; i < count; ++i) {
                if (offset + 4 > buffer.Length) {
                    // Not enough data for string offset
                    Array.Resize(ref values, i);
                    break;
                }
                
                var stringOffset = BitConverter.ToInt32(buffer, offset);
                
                if (stringOffset < 0 || stringsBaseOffset + stringOffset >= buffer.Length) {
                    values[i] = string.Empty;
                } else {
                    values[i] = buffer.ReadString(stringsBaseOffset + stringOffset);
                }
                offset += 4;
            }
            return values;
        }
        #endregion
    }
}
