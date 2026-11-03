"""Desktop shortcut entry point for the native transparent calendar."""
import subprocess
import sys
from pathlib import Path


if __name__ == "__main__":
    root = Path(__file__).resolve().parent
    python = Path(sys.executable).with_name("python.exe")
    subprocess.Popen([str(root / "TimeTreeWidget.exe"), str(python)], cwd=root,
                     creationflags=subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP)
