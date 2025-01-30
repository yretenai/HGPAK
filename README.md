# HelloPak

HelloPak Packer and Unpacker written in c#

WIP

## Format

Header:

```cxx
enum class pak_flags : uint64_t {
    compressed_stream = 1
};

struct pak_header {
    uint64_t magic; // HelloPak with 3 null bytes
    uint64_t version; // 2
    int64_t file_count;
    int64_t block_count;
    pak_flags flags;
    int64_t stream_start;
};

struct pak_file {
    char guid[16];
    int64_t offset; // in decompressed stream
    int64_t size;
};

struct HelloPak {
    pak_header header;
    pak_file files[header.file_count];
    uint64_t block_sizes[header.block_count];
};
```

Compression Type per Platform:

- Windows - ZStandard (as of the Worlds 2 Update)
- macOS - LZ4
- iPadOS - ??
- Switch - ??
- PS4 - ??
- PS5 - ??
- XONE - ??
- XSX - ??

## Usage

### Reading

```csharp
using var stream = new FileStream(pakPath, FileMode.Open, FileAccess.Read);
using var pak = new HelloPak(stream);

foreach(var (path, hash) in pak.Manifest) {
	using var namedFile = pak.OpenFile(hash);
}

using var knownFile = pak.OpenFile("SOMEFILE.BIN");
using var anotherKnownFile = pak.OpenFile(someMd5Hash);
```

### Writing

```csharp
using var stream = new FileStream(pakPath, FileMode.Open, FileAccess.Read);
using var existingPak = new HelloPak(stream);
using var builder = new HelloPakBuilder(existingPak); // it will automatically import all pak files if existingPak is not null.

builder.DeleteFile("SOMEFILE.BIN");
builder.AddFile("EXISTING.BIN", existingData); // if EXISTING.BIN exists, it will overwrite the data.
builder.AddFile("NEW.BIN", newData);

using var output = new FileStream("new.pak", FileMode.Create, FileAccess.ReadWrite);
builder.Build(output, "new.pak");
```
