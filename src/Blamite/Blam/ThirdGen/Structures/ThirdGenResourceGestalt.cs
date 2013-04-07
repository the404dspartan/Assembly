﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Blamite.Blam.Resources;
using Blamite.Blam.Util;
using Blamite.Flexibility;
using Blamite.IO;

namespace Blamite.Blam.ThirdGen.Resources
{
    public class ThirdGenResourceGestalt
    {
        private ITag _tag;
        private FileSegmentGroup _metaArea;
        private MetaAllocator _allocator;
        private BuildInformation _buildInfo;

        public ThirdGenResourceGestalt(ITag zoneTag, FileSegmentGroup metaArea, MetaAllocator allocator, BuildInformation buildInfo)
        {
            _tag = zoneTag;
            _metaArea = metaArea;
            _allocator = allocator;
            _buildInfo = buildInfo;
        }

        public IEnumerable<Resource> LoadResources(IReader reader, TagTable tags, IList<ResourcePointer> pointers)
        {
            var values = LoadTag(reader);
            byte[] infoBuffer = LoadResourceInfoBuffer(values, reader);

            int count = (int)values.GetInteger("number of resources");
            uint address = values.GetInteger("resource table address");
            var layout = _buildInfo.GetLayout("resource table entry");
            var entries = ReflexiveReader.ReadReflexive(reader, count, address, layout, _metaArea);
            return entries.Select((e, i) => LoadResource(e, i, tags, pointers, infoBuffer, reader));
        }

        public IList<ResourcePointer> SaveResources(ICollection<Resource> resources, IStream stream)
        {
            var values = LoadTag(stream);

            // Free everything
            FreeResources(values, stream);

            // Serialize each resource entry
            // This can't be lazily evaluated because allocations might cause the stream to expand
            uint infoOffset = 0;
            var pointers = new List<ResourcePointer>();
            var entries = new List<StructureValueCollection>();
            var fixupCache = new ReflexiveCache<ResourceFixup>();
            var defFixupCache = new ReflexiveCache<ResourceDefinitionFixup>();
            foreach (var resource in resources)
            {
                var entry = SerializeResource(resource, (resource.Location != null) ? pointers.Count : -1, (resource.Info != null) ? infoOffset : 0, stream);
                entries.Add(entry);

                // Save fixups
                SaveResourceFixups(resource.ResourceFixups, entry, stream, fixupCache);
                SaveDefinitionFixups(resource.DefinitionFixups, entry, stream, defFixupCache);

                // Update info offset and pointer info
                if (resource.Info != null)
                    infoOffset += (uint)resource.Info.Length;
                if (resource.Location != null)
                    pointers.Add(resource.Location);
            }

            // Write the reflexive and update the tag values
            var layout = _buildInfo.GetLayout("resource table entry");
            uint newAddress = ReflexiveWriter.WriteReflexive(entries, layout, _metaArea, _allocator, stream);
            values.SetInteger("number of resources", (uint)entries.Count);
            values.SetInteger("resource table address", newAddress);

            // Build and save the info buffer
            byte[] infoBuffer = BuildResourceInfoBuffer(resources);
            SaveResourceInfoBuffer(infoBuffer, values, stream);

            SaveTag(values, stream);
            return pointers;
        }

        private StructureValueCollection LoadTag(IReader reader)
        {
            reader.SeekTo(_tag.MetaLocation.AsOffset());
            return StructureReader.ReadStructure(reader, _buildInfo.GetLayout("resource gestalt"));
        }

        private void SaveTag(StructureValueCollection values, IWriter writer)
        {
            writer.SeekTo(_tag.MetaLocation.AsOffset());
            StructureWriter.WriteStructure(values, _buildInfo.GetLayout("resource gestalt"), writer);
        }

        private byte[] LoadResourceInfoBuffer(StructureValueCollection values, IReader reader)
        {
            int size = (int)values.GetInteger("resource info buffer size");
            uint address = values.GetInteger("resource info buffer address");
            if (size <= 0 || address == 0)
                return new byte[0];

            int offset = _metaArea.PointerToOffset(address);
            reader.SeekTo(offset);
            return reader.ReadBlock(size);
        }

        private byte[] BuildResourceInfoBuffer(IEnumerable<Resource> resources)
        {
            // Add up all of the sizes to compute the total buffer size
            int size = 0;
            foreach (var resource in resources.Where(r => r.Info != null))
                size += resource.Info.Length;

            // Now copy each info block into the buffer
            int offset = 0;
            byte[] result = new byte[size];
            foreach (var resource in resources.Where(r => r.Info != null))
            {
                Buffer.BlockCopy(resource.Info, 0, result, offset, resource.Info.Length);
                offset += resource.Info.Length;
            }

            return result;
        }

        private void SaveResourceInfoBuffer(byte[] buffer, StructureValueCollection values, IStream stream)
        {
            // Free the old info buffer
            int oldSize = (int)values.GetInteger("resource info buffer size");
            uint oldAddress = values.GetInteger("resource info buffer address");
            if (oldAddress >= 0 && oldSize > 0)
                _allocator.Free(oldAddress, oldSize);

            // Write a new one
            uint newAddress = 0;
            if (buffer.Length > 0)
            {
                newAddress = _allocator.Allocate(buffer.Length, stream);
                stream.SeekTo(_metaArea.PointerToOffset(newAddress));
                stream.WriteBlock(buffer);
            }

            // Update values
            values.SetInteger("resource info buffer size", (uint)buffer.Length);
            values.SetInteger("resource info buffer address", newAddress);
        }

        private Resource LoadResource(StructureValueCollection values, int index, TagTable tags, IList<ResourcePointer> pointers, byte[] infoBuffer, IReader reader)
        {
            Resource result = new Resource();

            DatumIndex parentTag = new DatumIndex(values.GetInteger("parent tag datum index"));
            result.ParentTag = parentTag.IsValid ? tags[parentTag] : null;
            ushort salt = (ushort)values.GetInteger("datum index salt");
            result.Index = new DatumIndex(salt, (ushort)index);
            result.Type = (int)values.GetInteger("resource type index");
            result.Flags = values.GetInteger("flags");

            int infoOffset = (int)values.GetInteger("resource info offset");
            int infoSize = (int)values.GetInteger("resource info size");
            if (infoSize > 0)
            {
                // Copy the section of the info buffer that the resource is pointing to
                result.Info = new byte[infoSize];
                Buffer.BlockCopy(infoBuffer, infoOffset, result.Info, 0, infoSize);
            }

            result.Unknown1 = (int)values.GetInteger("unknown 1");
            result.Unknown2 = (int)values.GetInteger("unknown 2");
            int segmentIndex = (int)values.GetInteger("segment index");
            result.Location = (segmentIndex >= 0) ? pointers[segmentIndex] : null;
            result.Unknown3 = (int)values.GetInteger("unknown 3");

            result.ResourceFixups.AddRange(LoadResourceFixups(values, reader));
            result.DefinitionFixups.AddRange(LoadDefinitionFixups(values, reader));
            return result;
        }

        private StructureValueCollection SerializeResource(Resource resource, int pointerIndex, uint infoOffset, IStream stream)
        {
            StructureValueCollection result = new StructureValueCollection();
            if (resource.ParentTag != null)
            {
                result.SetInteger("parent tag class magic", (uint)resource.ParentTag.Class.Magic);
                result.SetInteger("parent tag datum index", resource.ParentTag.Index.Value);
            }
            else
            {
                result.SetInteger("parent tag class magic", 0xFFFFFFFF);
                result.SetInteger("parent tag datum index", 0xFFFFFFFF);
            }
            result.SetInteger("datum index salt", (uint)resource.Index.Salt);
            result.SetInteger("resource type index", (uint)resource.Type);
            result.SetInteger("flags", resource.Flags);
            result.SetInteger("resource info offset", infoOffset);
            result.SetInteger("resource info size", (resource.Info != null) ? (uint)resource.Info.Length : 0);
            result.SetInteger("unknown 1", (uint)resource.Unknown1);
            result.SetInteger("unknown 2", (uint)resource.Unknown2);
            result.SetInteger("segment index", (uint)pointerIndex);
            result.SetInteger("unknown 3", (uint)resource.Unknown3);
            return result;
        }

        private IEnumerable<ResourceFixup> LoadResourceFixups(StructureValueCollection values, IReader reader)
        {
            int count = (int)values.GetInteger("number of resource fixups");
            uint address = values.GetInteger("resource fixup table address");
            var layout = _buildInfo.GetLayout("resource fixup entry");
            var entries = ReflexiveReader.ReadReflexive(reader, count, address, layout, _metaArea);
            return entries.Select(e => new ResourceFixup()
            {
                Offset = (int)e.GetInteger("offset"),
                Address = e.GetInteger("address")
            });
        }

        private void SaveResourceFixups(IList<ResourceFixup> fixups, StructureValueCollection values, IStream stream, ReflexiveCache<ResourceFixup> cache)
        {
            int oldCount = (int)values.GetIntegerOrDefault("number of resource fixups", 0);
            uint oldAddress = values.GetIntegerOrDefault("resource fixup table address", 0);
            var layout = _buildInfo.GetLayout("resource fixup entry");

            uint newAddress;
            if (!cache.TryGetAddress(fixups, out newAddress))
            {
                // Write a new reflexive
                var entries = fixups.Select(f => SerializeResourceFixup(f));
                newAddress = ReflexiveWriter.WriteReflexive(entries, oldCount, oldAddress, fixups.Count, layout, _metaArea, _allocator, stream);
                cache.Add(newAddress, fixups);
            }
            else if (oldAddress != 0 && oldCount > 0)
            {
                // Reflexive was cached - just free it
                _allocator.Free(oldAddress, oldCount * layout.Size);
            }

            values.SetInteger("number of resource fixups", (uint)fixups.Count);
            values.SetInteger("resource fixup table address", newAddress);
        }

        private StructureValueCollection SerializeResourceFixup(ResourceFixup fixup)
        {
            StructureValueCollection result = new StructureValueCollection();
            result.SetInteger("offset", (uint)fixup.Offset);
            result.SetInteger("address", (uint)fixup.Address);
            return result;
        }

        private IEnumerable<ResourceDefinitionFixup> LoadDefinitionFixups(StructureValueCollection values, IReader reader)
        {
            int count = (int)values.GetInteger("number of definition fixups");
            uint address = values.GetInteger("definition fixup table address");
            var layout = _buildInfo.GetLayout("definition fixup entry");
            var entries = ReflexiveReader.ReadReflexive(reader, count, address, layout, _metaArea);
            return entries.Select(e => new ResourceDefinitionFixup()
            {
                Offset = (int)e.GetInteger("offset"),
                Type = (int)e.GetInteger("type index")
            });
        }

        private StructureValueCollection SerializeDefinitionFixup(ResourceDefinitionFixup fixup)
        {
            StructureValueCollection result = new StructureValueCollection();
            result.SetInteger("offset", (uint)fixup.Offset);
            result.SetInteger("type index", (uint)fixup.Type);
            return result;
        }

        private void SaveDefinitionFixups(IList<ResourceDefinitionFixup> fixups, StructureValueCollection values, IStream stream, ReflexiveCache<ResourceDefinitionFixup> cache)
        {
            int oldCount = (int)values.GetIntegerOrDefault("number of definition fixups", 0);
            uint oldAddress = values.GetIntegerOrDefault("definition fixup table address", 0);
            var layout = _buildInfo.GetLayout("definition fixup entry");

            uint newAddress;
            if (!cache.TryGetAddress(fixups, out newAddress))
            {
                // Write a new reflexive
                var entries = fixups.Select(f => SerializeDefinitionFixup(f));
                newAddress = ReflexiveWriter.WriteReflexive(entries, oldCount, oldAddress, fixups.Count, layout, _metaArea, _allocator, stream);
                cache.Add(newAddress, fixups);
            }
            else if (oldAddress != 0 && oldCount > 0)
            {
                // Reflexive was cached - just free it
                _allocator.Free(oldAddress, oldCount * layout.Size);
            }

            values.SetInteger("number of definition fixups", (uint)fixups.Count);
            values.SetInteger("definition fixup table address", newAddress);
        }

        private void FreeResources(StructureValueCollection values, IReader reader)
        {
            int count = (int)values.GetInteger("number of resources");
            uint address = values.GetInteger("resource table address");
            var layout = _buildInfo.GetLayout("resource table entry");
            var entries = ReflexiveReader.ReadReflexive(reader, count, address, layout, _metaArea);
            foreach (var entry in entries)
                FreeResource(entry);

            int size = count * layout.Size;
            if (address >= 0 && size > 0)
                _allocator.Free(address, size);
        }

        private void FreeResource(StructureValueCollection values)
        {
            FreeResourceFixups(values);
            FreeDefinitionFixups(values);
        }

        private void FreeResourceFixups(StructureValueCollection values)
        {
            int count = (int)values.GetInteger("number of resource fixups");
            uint address = values.GetInteger("resource fixup table address");
            var layout = _buildInfo.GetLayout("resource fixup entry");
            int size = count * layout.Size;
            if (address >= 0 && size > 0)
                _allocator.Free(address, size);
        }

        private void FreeDefinitionFixups(StructureValueCollection values)
        {
            int count = (int)values.GetInteger("number of definition fixups");
            uint address = values.GetInteger("definition fixup table address");
            var layout = _buildInfo.GetLayout("definition fixup entry");
            int size = count * layout.Size;
            if (address >= 0 && size > 0)
                _allocator.Free(address, size);
        }
    }
}