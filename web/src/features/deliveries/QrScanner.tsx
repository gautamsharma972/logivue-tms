import { QrcodeOutlined } from '@ant-design/icons'
import { Alert, Button, Flex, Modal } from 'antd'
import { useEffect, useRef, useState } from 'react'

interface Detector {
  detect(source: CanvasImageSource): Promise<{ rawValue: string }[]>
}
type DetectorConstructor = new (options: { formats: string[] }) => Detector

/** The browser's own QR reader, where there is one (most Android browsers). Elsewhere the driver types the code. */
export const canScanQr = () => typeof window !== 'undefined' && 'BarcodeDetector' in window && !!navigator.mediaDevices?.getUserMedia

/** Pulls the delivery code out of what a QR holds: either the bare digits or a longer text that contains them. */
export function codeFromQr(text: string): string | null {
  const match = /\b\d{4,8}\b/.exec(text)
  return match ? match[0] : null
}

export function QrScanButton({ onCode }: { onCode: (code: string) => void }) {
  const [open, setOpen] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const video = useRef<HTMLVideoElement>(null)

  useEffect(() => {
    if (!open) return
    let stream: MediaStream | undefined
    let timer: number | undefined
    let stopped = false
    const run = async () => {
      try {
        stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' } })
        if (stopped || !video.current) return
        video.current.srcObject = stream
        await video.current.play()
        const detector = new (window as unknown as { BarcodeDetector: DetectorConstructor }).BarcodeDetector({ formats: ['qr_code'] })
        timer = window.setInterval(async () => {
          if (!video.current) return
          const found = await detector.detect(video.current).catch(() => [])
          const code = found.map((f) => codeFromQr(f.rawValue)).find((c) => c !== null)
          if (code) {
            onCode(code)
            setOpen(false)
          }
        }, 300)
      } catch {
        setError('The camera could not be opened. Type the code instead.')
      }
    }
    void run()
    return () => {
      stopped = true
      if (timer) window.clearInterval(timer)
      stream?.getTracks().forEach((t) => t.stop())
    }
  }, [open, onCode])

  return (
    <>
      <Button size="large" icon={<QrcodeOutlined />} onClick={() => { setError(null); setOpen(true) }}>Scan QR</Button>
      <Modal open={open} onCancel={() => setOpen(false)} footer={null} title="Scan the customer's QR code" destroyOnHidden>
        <Flex vertical gap={8}>
          {error && <Alert type="warning" showIcon title={error} />}
          <video ref={video} playsInline muted style={{ width: '100%', borderRadius: 8, background: '#000' }} />
        </Flex>
      </Modal>
    </>
  )
}
