"""Run inside Blender; reuses the cart round-trip and pivot checks."""
import importlib.util
from pathlib import Path
folder=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('cart_verification',folder/'verify_cart.py')
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
module.OUT=folder/'Revision2'
module.run()
