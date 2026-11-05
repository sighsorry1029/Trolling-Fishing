"""Run a managed probe in installed Valheim Mono without starting the game/Unity scene."""
import ctypes
import os
import pathlib
import sys

game = pathlib.Path(os.environ.get("VALHEIM_GAME", r"C:\Program Files (x86)\Steam\steamapps\common\Valheim"))
runtime = game / "MonoBleedingEdge" / "EmbedRuntime"
managed = game / "valheim_Data" / "Managed"
with os.add_dll_directory(str(runtime)):
    mono = ctypes.CDLL(str(runtime / "mono-2.0-bdwgc.dll"))

def api(name, result, *args):
    function = getattr(mono, name)
    function.restype = result
    function.argtypes = args
    return function

pointer, string = ctypes.c_void_p, ctypes.c_char_p
api("mono_set_dirs", None, string, string)(str(managed).encode(), str(game / "MonoBleedingEdge" / "etc").encode())
api("mono_set_assemblies_path", None, string)(str(managed).encode())
api("mono_config_parse", None, string)(None)
domain = api("mono_jit_init_version", pointer, string, string)(b"TrollingFishingCompatibilityProbe", b"v4.0.30319")
probe = pathlib.Path(sys.argv[1]).resolve()
assembly = api("mono_domain_assembly_open", pointer, pointer, string)(domain, str(probe).encode())
if not domain or not assembly:
    raise RuntimeError("Isolated Mono/probe initialization failed")
arguments = [str(probe).encode()] + [arg.encode() for arg in sys.argv[2:]]
argv = (string * len(arguments))(*arguments)
sys.exit(api("mono_jit_exec", ctypes.c_int, pointer, pointer, ctypes.c_int, ctypes.POINTER(string))(
    domain, assembly, len(arguments), argv))
