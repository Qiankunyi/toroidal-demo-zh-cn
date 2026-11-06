"""Convert a local source-text TSV to the plugin's hash-keyed format."""

from argparse import ArgumentParser
from base64 import b64encode
from hashlib import sha256
from pathlib import Path


def convert(source: Path, destination: Path) -> None:
    entries: dict[str, str] = {}
    for number, line in enumerate(source.read_text(encoding="utf-8-sig").split("\n"), 1):
        if not line or line.startswith("//"):
            continue
        original, separator, translation = line.partition("\t")
        if not original or not separator or not translation:
            raise ValueError(f"Invalid translation row {number} in {source.name}")
        decoded_key = original.replace(r"\n", "\n")
        key = b64encode(sha256(decoded_key.encode("utf-8")).digest()).decode("ascii")
        if key in entries:
            raise ValueError(f"Duplicate translation key at row {number} in {source.name}")
        entries[key] = translation
    with destination.open("x", encoding="utf-8", newline="\n") as table:
        table.write("// key-format=sha256-base64\n")
        for key, translation in entries.items():
            table.write(f"{key}\t{translation}\n")


if __name__ == "__main__":
    parser = ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    convert(args.source, args.destination)
