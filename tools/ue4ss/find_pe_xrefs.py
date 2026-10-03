import struct
import sys
from pathlib import Path


def main() -> int:
    image_path = Path(sys.argv[1])
    queries = [item.encode("ascii") for item in sys.argv[2:]]
    data = image_path.read_bytes()

    pe_offset = struct.unpack_from("<I", data, 0x3C)[0]
    section_count = struct.unpack_from("<H", data, pe_offset + 6)[0]
    optional_size = struct.unpack_from("<H", data, pe_offset + 20)[0]
    section_offset = pe_offset + 24 + optional_size
    sections = []

    for index in range(section_count):
        offset = section_offset + index * 40
        name = data[offset : offset + 8].split(b"\0")[0].decode(errors="replace")
        virtual_size, virtual_address, raw_size, raw_pointer = struct.unpack_from(
            "<IIII", data, offset + 8
        )
        sections.append((name, virtual_address, virtual_size, raw_pointer, raw_size))

    text = next(section for section in sections if section[0] == ".text")
    _, text_rva, _, text_raw, text_size = text
    text_data = data[text_raw : text_raw + text_size]

    def file_offset_to_rva(file_offset: int) -> int:
        for _, virtual_address, _, raw_pointer, raw_size in sections:
            if raw_pointer <= file_offset < raw_pointer + raw_size:
                return virtual_address + file_offset - raw_pointer
        raise ValueError(f"File offset 0x{file_offset:X} is outside mapped sections")

    for query in queries:
        file_offset = data.find(query)
        if file_offset < 0:
            print(f"NOT FOUND: {query.decode('ascii')}")
            continue

        target_rva = file_offset_to_rva(file_offset)
        xrefs = []
        for index in range(len(text_data) - 4):
            displacement = struct.unpack_from("<i", text_data, index)[0]
            if text_rva + index + 4 + displacement == target_rva:
                xrefs.append(text_rva + index)

        print(query.decode("ascii"))
        print(f"  file offset: 0x{file_offset:X}")
        print(f"  RVA: 0x{target_rva:X}")
        print("  displacement RVAs: " + ", ".join(f"0x{x:X}" for x in xrefs))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
