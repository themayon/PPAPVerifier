import glob
import subprocess

wheels = glob.glob("wheelhouse/*.whl")
for whl in wheels:
    subprocess.run(["pip", "install", "--no-index", "--force-reinstall", whl])