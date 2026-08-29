import sqlite3
import glob
import os
import struct

# 히라가나, 가타카나 및 기호 3자리 코드 매핑 (새로운 C# CharacterDatabase 규칙과 100% 동기화)
# 100의 자리(0:청음, 1:탁음, 2:반탁음, 3:스테가나, 4:숫자 및 기호)
CHAR_TO_CODE = {
    'あ':   0, 'い':   1, 'う':   2, 'え':   3, 'お':   4,
    'か':  10, 'き':  11, 'く':  12, 'け':  13, 'こ':  14,
    'さ':  20, 'し':  21, 'す':  22, 'せ':  23, 'そ':  24,
    'た':  30, 'ち':  31, 'つ':  32, 'て':  33, 'と':  34,
    'は':  40, 'ひ':  41, 'ふ':  42, 'へ':  43, 'ほ':  44,
    'な':  50, 'に':  51, 'ぬ':  52, 'ね':  53, 'の':  54,
    'ま':  60, 'み':  61, 'む':  62, 'め':  63, 'も':  64,
    'ら':  70, 'り':  71, 'る':  72, 'れ':  73, 'ろ':  74,
    'や':  80,            'ゆ':  82,            'よ':  84,
    'わ':  90, 'ゐ':  91, 'ん':  92, 'ゑ':  93, 'を':  94,
    'が': 110, 'ぎ': 111, 'ぐ': 112, 'げ': 113, 'ご': 114,
    'ざ': 120, 'じ': 121, 'ず': 122, 'ぜ': 123, 'ぞ': 124,
    'だ': 130, 'ぢ': 131, 'づ': 132, 'で': 133, 'ど': 134,
    'ば': 140, 'び': 141, 'ぶ': 142, 'べ': 143, 'ぼ': 144,
    'ぱ': 240, 'ぴ': 241, 'ぷ': 242, 'ぺ': 243, 'ぽ': 244,
    'ぁ': 300, 'ぃ': 301, 'ぅ': 302, 'ぇ': 303, 'ぉ': 304,
    'ゕ': 310,                       'ゖ': 313, 
                          'っ': 332,
    'ゃ': 380,            'ゅ': 382,            'ょ': 384, 
    'ゎ': 390,            'ゔ': 102,
    
    # 가타카나 매핑 (+5 적용)
    'ア':   5, 'イ':   6, 'ウ':   7, 'エ':   8, 'オ':   9,
    'カ':  15, 'キ':  16, 'ク':  17, 'ケ':  18, 'コ':  19,
    'サ':  25, 'シ':  26, 'ス':  27, 'セ':  28, 'ソ':  29,
    'タ':  35, 'チ':  36, 'ツ':  37, 'テ':  38, 'ト':  39,
    'ハ':  45, 'ヒ':  46, 'フ':  47, 'ヘ':  48, 'ホ':  49,
    'ナ':  55, 'ニ':  56, 'ヌ':  57, 'ネ':  58, 'ノ':  59,
    'マ':  65, 'ミ':  66, 'ム':  67, 'メ':  68, 'モ':  69,
    'ラ':  75, 'リ':  76, 'ル':  77, 'レ':  78, 'ロ':  79,
    'ヤ':  85,            'ユ':  87,            'ヨ':  89,
    'ワ':  95, 'ヰ':  96, 'ン':  97, 'ヱ':  98, 'ヲ':  99,
    'ガ': 115, 'ギ': 116, 'グ': 117, 'ゲ': 118, 'ゴ': 119,
    'ザ': 125, 'ジ': 126, 'ズ': 127, 'ゼ': 128, 'ゾ': 129,
    'ダ': 135, 'ヂ': 136, 'ヅ': 137, 'デ': 138, 'ド': 139,
    'バ': 145, 'ビ': 146, 'ブ': 147, 'ベ': 148, 'ボ': 149,
    'パ': 245, 'ピ': 246, 'プ': 247, 'ペ': 248, 'ポ': 249,
    'ァ': 305, 'ィ': 306, 'ゥ': 307, 'ェ': 308, 'ォ': 309,
    'ヵ': 315,                       'ヶ': 318, 
                          'ッ': 337,
    'ャ': 385,            'ュ': 387,            'ョ': 389, 
    'ヮ': 395,            'ヴ': 107,

    # 숫자 및 특수기호 매핑 (400 ~ 445)
    '0': 400, ')': 405,
    '1': 401, '!': 406,
    '2': 402, '@': 407,
    '3': 403, '#': 408,
    '4': 404, '$': 409,
    '5': 410, '%': 415,
    '6': 411, '^': 416,
    '7': 412, '&': 417,
    '8': 413, '*': 418,
    '9': 414, '(': 419,
    ',': 420, 'ー': 425,
    '.': 421, '・': 426,
    '々': 422, '～': 427,
    '。': 423, '?': 428,
    '、': 424, ':': 429,
    '「': 430, '『': 435,
    '」': 431, '』': 436,
    '円': 432, '¥': 437,
    '-': 433, '_': 438,
    '=': 434, '+': 439,
    '\'': 440, '\"': 445,
}

def reading_to_blob(reading_str):
    blob = bytearray()
    for ch in reading_str:
        # DB에 저장될 때, 400~440 영역 문자도 안전하게 매핑
        code = CHAR_TO_CODE.get(ch, ord(ch))
        blob.extend(struct.pack('<H', code))
    return bytes(blob)

db_path = "mozc_dict_connect.db"
if os.path.exists(db_path):
    os.remove(db_path)

conn = sqlite3.connect(db_path)
cursor = conn.cursor()
cursor.execute("PRAGMA synchronous = OFF;")
cursor.execute("PRAGMA journal_mode = MEMORY;")

cursor.execute("""
CREATE TABLE dictionary (
    reading BLOB NOT NULL,
    kanji TEXT NOT NULL,
    left_id INTEGER NOT NULL,
    right_id INTEGER NOT NULL,
    cost INTEGER NOT NULL
);
""")

dict_files = sorted(glob.glob("dictionary*.txt"))
batch_data = []

print("[1/2] Mozc 단어 사전 SQLite 생성 시작...")
for file_path in dict_files:
    print(f"  - 처리 중: {file_path}")
    with open(file_path, "r", encoding="utf-8") as f:
        for line in f:
            line_str = line.rstrip("\r\n")
            parts = line_str.split("\t")
            if len(parts) >= 5:
                reading_blob = reading_to_blob(parts[0])
                batch_data.append((reading_blob, parts[4], int(parts[1]), int(parts[2]), int(parts[3])))
                
            if len(batch_data) >= 100000:
                cursor.executemany("INSERT INTO dictionary VALUES (?, ?, ?, ?, ?)", batch_data)
                conn.commit()
                batch_data.clear()

if batch_data:
    cursor.executemany("INSERT INTO dictionary VALUES (?, ?, ?, ?, ?)", batch_data)
    conn.commit()

cursor.execute("CREATE INDEX idx_reading_cost ON dictionary(reading, cost);")
conn.commit()

# Connection Matrix 처리
matrix_txt = "connection_single_column.txt"
if os.path.exists(matrix_txt):
    matrix_bytes = bytearray()
    with open(matrix_txt, "r", encoding="utf-8") as f_in:
        matrix_size = int(f_in.readline().strip())
        for line in f_in:
            cost = int(line.strip())
            matrix_bytes.extend(struct.pack("<h", cost))
            
    cursor.execute("""
    CREATE TABLE IF NOT EXISTS matrix_metadata (
        id INTEGER PRIMARY KEY,
        matrix_size INTEGER NOT NULL,
        data BLOB NOT NULL
    )
    """)
    cursor.execute("INSERT OR REPLACE INTO matrix_metadata VALUES (1, ?, ?)", (matrix_size, bytes(matrix_bytes)))
    conn.commit()

cursor.execute("VACUUM;")
conn.close()
print("DB 생성 완료")