// Generates the PWA install icons (192/512, regular + maskable) as valid PNGs
// with zero dependencies (uses Node's built-in zlib). Run once from the client
// root: `node scripts/generate-icons.mjs`.
//
// The icon is a full-bleed brand-blue square with a white "H" glyph kept inside
// the maskable safe zone (content within the central ~80% region).
import { deflateSync } from 'node:zlib'
import { mkdirSync, writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..')
const OUT_DIR = join(ROOT, 'public', 'icons')

const BRAND = [0x1d, 0x4e, 0xd8, 0xff] // #1d4ed8
const GLYPH = [0xff, 0xff, 0xff, 0xff] // #ffffff

// --- Minimal PNG encoder -----------------------------------------------------

let crcTable
function crc32(buf) {
  if (!crcTable) {
    crcTable = new Int32Array(256)
    for (let n = 0; n < 256; n++) {
      let c = n
      for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1
      crcTable[n] = c
    }
  }
  let crc = -1
  for (let i = 0; i < buf.length; i++) crc = (crc >>> 8) ^ crcTable[(crc ^ buf[i]) & 0xff]
  return (crc ^ -1) >>> 0
}

function chunk(type, data) {
  const len = Buffer.alloc(4)
  len.writeUInt32BE(data.length, 0)
  const typeBuf = Buffer.from(type, 'ascii')
  const crc = Buffer.alloc(4)
  crc.writeUInt32BE(crc32(Buffer.concat([typeBuf, data])), 0)
  return Buffer.concat([len, typeBuf, data, crc])
}

function encodePng(size, pixelFn) {
  const ihdr = Buffer.alloc(13)
  ihdr.writeUInt32BE(size, 0)
  ihdr.writeUInt32BE(size, 4)
  ihdr[8] = 8 // bit depth
  ihdr[9] = 6 // colour type: RGBA
  const rows = []
  for (let y = 0; y < size; y++) {
    const row = Buffer.alloc(1 + size * 4)
    row[0] = 0 // filter: none
    for (let x = 0; x < size; x++) {
      const [r, g, b, a] = pixelFn(x, y, size)
      const o = 1 + x * 4
      row[o] = r
      row[o + 1] = g
      row[o + 2] = b
      row[o + 3] = a
    }
    rows.push(row)
  }
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr),
    chunk('IDAT', deflateSync(Buffer.concat(rows), { level: 9 })),
    chunk('IEND', Buffer.alloc(0)),
  ])
}

// --- Glyph -------------------------------------------------------------------

// "H" within a 30%..70% band (well inside the 80% maskable safe zone).
function pixel(x, y, size) {
  const u = x / size
  const v = y / size
  const barLeft = u >= 0.3 && u <= 0.39
  const barRight = u >= 0.61 && u <= 0.7
  const barMid = v >= 0.46 && v <= 0.54 && u >= 0.3 && u <= 0.7
  return barLeft || barRight || barMid ? GLYPH : BRAND
}

// --- Write -------------------------------------------------------------------

mkdirSync(OUT_DIR, { recursive: true })

const targets = [
  { name: 'icon-192.png', size: 192 },
  { name: 'icon-512.png', size: 512 },
  { name: 'icon-maskable-192.png', size: 192 },
  { name: 'icon-maskable-512.png', size: 512 },
]

for (const { name, size } of targets) {
  writeFileSync(join(OUT_DIR, name), encodePng(size, pixel))
  console.log(`wrote ${join('public', 'icons', name)} (${size}x${size})`)
}
