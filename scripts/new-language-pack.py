"""Create a partial, data-only translation template with stable English message IDs."""
import argparse
import json
from pathlib import Path
import re

root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--code',required=True)
parser.add_argument('--native-name',required=True)
parser.add_argument('--english-name',required=True)
parser.add_argument('--culture',required=True)
parser.add_argument('--direction',choices=['ltr','rtl'],default='ltr')
parser.add_argument('--output',type=Path,required=True)
args=parser.parse_args()
if not re.fullmatch(r'[a-z]{2,3}(?:-[A-Za-z0-9]{2,8})*',args.code):
    parser.error('Use a portable BCP 47 language tag.')
if args.output.exists():
    parser.error('Output already exists; choose a new file.')
english=json.loads((root/'localization/packs/en.json').read_text(encoding='utf-8'))
pack={'schemaVersion':1,'code':args.code,'nativeName':args.native_name,'englishName':args.english_name,
      'culture':args.culture,'direction':args.direction,'strings':{}}
args.output.parent.mkdir(parents=True,exist_ok=True)
args.output.write_text(json.dumps(pack,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Template created. Translate at least one message from localization/packs/en.json before importing; omitted messages use English.')
