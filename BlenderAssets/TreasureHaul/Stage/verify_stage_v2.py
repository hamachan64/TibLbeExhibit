import importlib.util, json
from pathlib import Path
folder=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('verify_stage_base',folder/'verify_stage.py')
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
module.OUT=folder/'Revision2'
module.run()
report=module.OUT/'verification.json'
data=json.loads(report.read_text(encoding='utf-8'))
data['unity_compile']='not run; model-only revision, Unity integration out of scope'
report.write_text(json.dumps(data,indent=2),encoding='utf-8')
