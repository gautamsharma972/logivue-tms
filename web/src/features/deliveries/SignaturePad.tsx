import { Button, Flex } from 'antd'
import { useEffect, useRef, useState } from 'react'

/** A finger-friendly signature box. Hands back a PNG; the server separately refuses one with nothing drawn on it. */
export function SignaturePad({ onChange }: { onChange: (png: Blob | null) => void }) {
  const canvas = useRef<HTMLCanvasElement>(null)
  const drawing = useRef(false)
  const [inked, setInked] = useState(false)

  useEffect(() => {
    const c = canvas.current
    if (!c) return
    const ctx = c.getContext('2d')
    if (ctx) {
      ctx.lineWidth = 2.5
      ctx.lineCap = 'round'
      ctx.strokeStyle = '#0a0a50'
    }
  }, [])

  const point = (e: React.PointerEvent<HTMLCanvasElement>) => {
    const rect = e.currentTarget.getBoundingClientRect()
    return { x: ((e.clientX - rect.left) / rect.width) * e.currentTarget.width, y: ((e.clientY - rect.top) / rect.height) * e.currentTarget.height }
  }

  const start = (e: React.PointerEvent<HTMLCanvasElement>) => {
    e.currentTarget.setPointerCapture?.(e.pointerId)
    drawing.current = true
    const ctx = e.currentTarget.getContext('2d')
    const { x, y } = point(e)
    ctx?.beginPath()
    ctx?.moveTo(x, y)
  }

  const move = (e: React.PointerEvent<HTMLCanvasElement>) => {
    if (!drawing.current) return
    const ctx = e.currentTarget.getContext('2d')
    const { x, y } = point(e)
    ctx?.lineTo(x, y)
    ctx?.stroke()
    setInked(true)
  }

  const end = () => {
    if (!drawing.current) return
    drawing.current = false
    canvas.current?.toBlob((blob) => onChange(blob), 'image/png')
  }

  const clear = () => {
    const c = canvas.current
    c?.getContext('2d')?.clearRect(0, 0, c.width, c.height)
    setInked(false)
    onChange(null)
  }

  return (
    <Flex vertical gap={8}>
      <canvas
        ref={canvas} width={600} height={220} aria-label="Signature box"
        style={{ width: '100%', height: 150, border: '1px dashed #8c8c8c', borderRadius: 8, touchAction: 'none', background: 'rgba(127,127,127,0.06)' }}
        onPointerDown={start} onPointerMove={move} onPointerUp={end} onPointerLeave={end}
      />
      <Button onClick={clear} disabled={!inked}>Clear signature</Button>
    </Flex>
  )
}
