import argparse
import hashlib
import json
import logging
import os
import requests
import sys
import time
from tqdm import tqdm
from urllib.parse import urljoin
from concurrent.futures import ThreadPoolExecutor, as_completed
import xml.etree.ElementTree as ET
from typing import List, Dict, Any, Optional

BASE_NUS_URL = "http://ccs.cdn.wup.shop.nintendo.net/ccs/download/"

def print_banner():
    print(r"""
  ____  _   _ ____     ____            _                 
 |  _ \| \ | |  _ \   / ___| _   _ ___| |_ ___ _ __ ___  
 | | | |  \| | | | |  \___ \| | | / __| __/ _ \ '_ ` _ \ 
 | |_| | |\  | |_| |   ___) | |_| \__ \ ||  __/ | | | | |
 |____/|_| \_|____/   |____/ \__, |___/\__\___|_| |_| |_|
                             |___/     Wii U NUS Explorer
    """)

def sha256_hash(filename: str) -> str:
    h = hashlib.sha256()
    with open(filename, 'rb') as f:
        for chunk in iter(lambda: f.read(8192), b""):
            h.update(chunk)
    return h.hexdigest()

def download_file_with_progress(url: str, filename: str) -> bool:
    try:
        response = requests.get(url, stream=True)
        if response.status_code == 404:
            logging.warning(f"Content not found (404): {url}")
            return False
        response.raise_for_status()
        total_size = int(response.headers.get("content-length", 0))
        start = time.time()

        with open(filename, "wb") as f, tqdm(
            total=total_size, unit="B", unit_scale=True, desc=os.path.basename(filename)
        ) as pbar:
            for chunk in response.iter_content(1024):
                if chunk:
                    f.write(chunk)
                    pbar.update(len(chunk))

        elapsed = time.time() - start
        speed = total_size / elapsed / 1024 if elapsed > 0 else 0
        print(f"Finished in {elapsed:.2f}s ({speed:.2f} KB/s)")
        return True
    except Exception as e:
        logging.error(f"Download failed: {e}")
        return False

def get_tmd_version(raw_tmd: bytes) -> int:
    """Extracts the version number from TMD (offset 0x18, 1 byte)."""
    if len(raw_tmd) > 0x18:
        return raw_tmd[0x18]
    return -1

def parse_tmd(raw: bytes) -> List[Dict[str, Any]]:
    """Parses the contents of a TMD file and returns a list of content records."""
    entries: List[Dict[str, Any]] = []
    if len(raw) < 0xB04:
        raise ValueError("TMD too short or malformed (header too small)")
    content_count = int.from_bytes(raw[0x9E:0xA0], "big")
    records_offset = 0xB04
    for i in range(content_count):
        offset = records_offset + i * 0x30
        if len(raw) < offset + 0x30:
            logging.warning(f"TMD appears truncated at entry {i}.")
            break
        content_id = raw[offset:offset+4].hex()
        index = int.from_bytes(raw[offset+4:offset+6], "big")
        # Correct: 8 bytes for size
        size = int.from_bytes(raw[offset+8:offset+16], "big")
        hash_bytes = raw[offset+0x10:offset+0x30]
        entries.append({
            "index": index,
            "content_id": content_id,
            "size": size,
            "hash": hash_bytes.hex()
        })
    return entries

def save_json_report(title_id: str, entries: List[Dict[str, Any]], stats: Dict[str, int], filename: str, title_name: str = "Unknown Title"):
    data = {
        "title_id": title_id,
        "title_name": title_name,
        "summary": stats,
        "contents": entries
    }
    with open(filename, "w") as f:
        json.dump(data, f, indent=2)
    print(f"[+] Saved JSON to {filename}")

def process_content(entry: Dict[str, Any], base_url: str, title_id: str, download_dir: str, force: bool, nohash: bool) -> tuple:
    content_id = entry["content_id"]
    url = base_url + content_id
    filename = os.path.join(download_dir, f"{title_id}_{content_id}.app")

    if os.path.exists(filename) and not force:
        if not nohash:
            if sha256_hash(filename) == entry["hash"]:
                return (content_id, "skipped", filename)
        else:
            return (content_id, "skipped", filename)

    success = download_file_with_progress(url, filename)
    if not success:
        return (content_id, "failed", filename)

    if not nohash:
        actual_hash = sha256_hash(filename)
        if actual_hash != entry["hash"]:
            logging.error(f"Hash mismatch for {filename}. Expected {entry['hash']}, got {actual_hash}")
            # Optional: Remove the file if hash fails
            try:
                os.remove(filename)
                logging.info(f"Removed corrupted file {filename}")
            except Exception as e:
                logging.warning(f"Could not remove file {filename}: {e}")
            return (content_id, "hash_fail", filename)
    # If --nohash is set, don't check hash at all
    return (content_id, "ok", filename)

def fetch_tmd(title_id: str) -> Optional[bytes]:
    url = urljoin(BASE_NUS_URL, f"{title_id}/tmd")
    try:
        response = requests.get(url)
        if response.status_code == 404:
            logging.error(f"TMD not found for {title_id} (404)")
            return None
        response.raise_for_status()
        return response.content
    except requests.RequestException as e:
        logging.error(f"TMD fetch failed for {title_id}: {e}")
        return None

def fetch_title_db() -> Dict[str, str]:
    url = "https://3dsdb.com/xml.php"
    try:
        response = requests.get(url)
        response.raise_for_status()
        root = ET.fromstring(response.content)
        title_dict = {}
        for title in root.findall('title'):
            title_id = title.find('titleid').text.upper()
            name = title.find('name').text
            title_dict[title_id] = name
        return title_dict
    except Exception as e:
        logging.warning(f"Failed to fetch title database: {e}")
        return {}

def main():
    print_banner()

    parser = argparse.ArgumentParser(description="Explore and download 3DS/Wii U title contents from Nintendo's NUS")
    parser.add_argument("title_ids", nargs="+", help="One or more 3DS/Wii U title IDs")
    parser.add_argument("--download-dir", default="downloads", help="Directory to save downloaded contents")
    parser.add_argument("--force", action="store_true", help="Force re-download even if hash is valid")
    parser.add_argument("--verbose", action="store_true", help="Enable verbose logging")
    parser.add_argument("--quiet", action="store_true", help="Suppress non-error output")
    parser.add_argument("--json", action="store_true", help="Output result as JSON report")
    parser.add_argument("--nohash", action="store_true", help="Disable hash checking")

    args = parser.parse_args()

    os.makedirs(args.download_dir, exist_ok=True)

    log_level = logging.ERROR if args.quiet else logging.DEBUG if args.verbose else logging.INFO
    logging.basicConfig(level=log_level, format="[%(levelname)s] %(message)s")

    title_dict = fetch_title_db()

    for title_id in args.title_ids:
        title_name = title_dict.get(title_id.upper(), "Unknown Title")
        try:
            logging.info(f"Fetching TMD for {title_id} ({title_name})")
            raw_tmd = fetch_tmd(title_id)
            if not raw_tmd:
                continue  # Skip to next title ID

            # TMD Version reporting
            tmd_version = get_tmd_version(raw_tmd)
            print(f"[+] {title_name} ({title_id}) - TMD version: {tmd_version}")

            entries = parse_tmd(raw_tmd)
            print(f"[+] {title_name} ({title_id}) - {len(entries)} contents")

            stats = {
                "ok": 0,
                "failed": 0,
                "skipped": 0,
                "hash_fail": 0
            }

            base_url = urljoin(BASE_NUS_URL, f"{title_id}/")
            with ThreadPoolExecutor(max_workers=4) as executor:
                futures = [
                    executor.submit(process_content, entry, base_url, title_id, args.download_dir, args.force, args.nohash)
                    for entry in entries
                ]

                for future in as_completed(futures):
                    content_id, status, _ = future.result()
                    stats[status] += 1
                    if not args.quiet:
                        print(f"Content {content_id}: {status}")

            print(f"[=] {title_name} Summary: {stats}")

            if args.json:
                save_json_report(title_id, entries, stats, f"{title_id}_report.json", title_name)

        except Exception as e:
            logging.error(f"Error processing {title_id}: {e}")

if __name__ == "__main__":
    main()