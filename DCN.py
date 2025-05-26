import os
import subprocess
import sys
import shutil

def find_titles(download_dir):
    return [
        f for f in os.listdir(download_dir)
        if os.path.isdir(os.path.join(download_dir, f))
    ]

def rename_tik_to_title(title_dir):
    for fname in os.listdir(title_dir):
        if fname.endswith(".tik"):
            old_path = os.path.join(title_dir, fname)
            new_path = os.path.join(title_dir, "title.tik")
            
            if os.path.exists(new_path):
                print(f"[!] 'title.tik' already exists in {title_dir}, skipping rename")
                return False
            
            os.rename(old_path, new_path)
            print(f"[>] Renamed {fname} to title.tik")
            return True
    return False

def run_cdecrypt(title_dir, decrypted_base_dir, cdecrypt_path="CDecrypt.exe"):
    tmd_file = None
    tik_renamed = rename_tik_to_title(title_dir)

    for fname in os.listdir(title_dir):
        if fname.endswith(".tmd"):
            tmd_file = fname

    if not tmd_file:
        print(f"[!] Skipping {title_dir}: .tmd not found")
        return

    tik_file = "title.tik" if tik_renamed else None
    if not tik_file:
        print(f"[!] Skipping {title_dir}: No .tik file found/renamed")
        return

    # Run CDecrypt
    tmd_path = os.path.join(title_dir, tmd_file)
    tik_path = os.path.join(title_dir, tik_file)

    print(f"[+] Decrypting {title_dir}")

    cmd = [
        cdecrypt_path,
        tmd_path,
        tik_path,
        title_dir  # Decrypt in-place
    ]
    try:
        subprocess.run(cmd, check=True)
        print(f"[=] Decryption complete in {title_dir}")
    except Exception as e:
        print(f"[!] Decryption failed for {title_dir}: {e}")
        return

    # Move only specified subdirectories to decrypted/
    out_dir = os.path.join(decrypted_base_dir, os.path.basename(title_dir))
    os.makedirs(out_dir, exist_ok=True)

    dirs_to_move = ['code', 'content', 'meta']  # Only these directories will be moved
    
    for item in dirs_to_move:
        src = os.path.join(title_dir, item)
        if os.path.exists(src) and os.path.isdir(src):
            dst = os.path.join(out_dir, item)
            if os.path.exists(dst):
                shutil.rmtree(dst)  # Remove existing to avoid conflicts
            shutil.move(src, dst)
            print(f"[>] Moved directory {item} to {out_dir}")
        else:
            print(f"[!] Directory {item} not found in {title_dir}")

def main():
    downloads_dir = "downloads"
    decrypted_dir = "decrypted"
    cdecrypt_exe = "CDecrypt.exe" if os.name == "nt" else "./CDecrypt"

    if not os.path.isdir(downloads_dir):
        print(f"Download directory '{downloads_dir}' not found.")
        sys.exit(1)
    os.makedirs(decrypted_dir, exist_ok=True)

    for title_subdir in find_titles(downloads_dir):
        title_dir = os.path.join(downloads_dir, title_subdir)
        run_cdecrypt(title_dir, decrypted_dir, cdecrypt_exe)

if __name__ == "__main__":
    main()
