import { CameraOutlined, DeleteOutlined, FilePdfOutlined, UploadOutlined } from '@ant-design/icons'
import { Button, Flex, Modal, Typography, message } from 'antd'
import { useEffect, useMemo, useRef, useState } from 'react'
import { shrinkImage } from './imageTools'

/** The browser can open the camera itself (needs https or localhost). Where it cannot, a phone's own camera is reached through a file input instead. */
const canOpenCamera = () => window.isSecureContext && typeof navigator.mediaDevices?.getUserMedia === 'function'

/** Live webcam view with a capture button, for computers. */
function WebcamModal({ onClose, onPhoto, onFallback }: { onClose: () => void; onPhoto: (file: File) => void; onFallback: () => void }) {
  const video = useRef<HTMLVideoElement>(null)
  const [error, setError] = useState<string | null>(null)
  const [ready, setReady] = useState(false)
  useEffect(() => {
    let stream: MediaStream | null = null
    let cancelled = false
    navigator.mediaDevices
      ?.getUserMedia({ video: { facingMode: 'environment', width: { ideal: 1920 } }, audio: false })
      .then((s) => {
        if (cancelled) return s.getTracks().forEach((t) => t.stop())
        stream = s
        if (video.current) {
          video.current.srcObject = s
          void video.current.play()
        }
        setReady(true)
      })
      .catch(() => setError(navigator.mediaDevices ? 'The camera could not be opened. Allow camera access for this site in the browser, or use Upload.' : 'This browser cannot open the camera here. Use Upload instead.'))
    return () => {
      cancelled = true
      stream?.getTracks().forEach((t) => t.stop())
    }
  }, [])
  const snap = () => {
    const v = video.current
    if (!v || !v.videoWidth) return
    const canvas = document.createElement('canvas')
    canvas.width = v.videoWidth
    canvas.height = v.videoHeight
    canvas.getContext('2d')?.drawImage(v, 0, 0)
    canvas.toBlob((blob) => {
      if (!blob) return
      onPhoto(new File([blob], `photo-${new Date().toISOString().replace(/[:.]/g, '-')}.jpg`, { type: 'image/jpeg' }))
    }, 'image/jpeg', 0.9)
  }
  return (
    <Modal open title="Take a photo" onCancel={onClose} destroyOnHidden footer={<Flex gap={8} justify="flex-end"><Button onClick={onClose}>Done</Button><Button type="primary" icon={<CameraOutlined />} disabled={!ready} onClick={snap}>Capture</Button></Flex>}>
      {error ? <Flex vertical gap={12} align="flex-start"><Typography.Text type="danger">{error}</Typography.Text><Button onClick={onFallback}>Use the device's camera app</Button></Flex> : <video ref={video} playsInline muted style={{ width: '100%', borderRadius: 8, background: '#000' }} />}
      <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>Each capture is added to the list. Press Done when you have all the pages.</Typography.Paragraph>
    </Modal>
  )
}

interface Props {
  files: File[]
  onChange: (files: File[]) => void
  max: number
}

/** Take a photo with the phone's camera, or pick photos and PDFs already on the device, up to a limit. */
export function PhotoPicker({ files, onChange, max }: Props) {
  const camera = useRef<HTMLInputElement>(null)
  const gallery = useRef<HTMLInputElement>(null)
  const previews = useMemo(() => files.map((f) => (f.type.startsWith('image/') ? URL.createObjectURL(f) : null)), [files])
  useEffect(() => () => previews.forEach((p) => p && URL.revokeObjectURL(p)), [previews])
  const [webcam, setWebcam] = useState(false)
  const full = files.length >= max
  const add = async (picked: FileList | File[] | null) => {
    if (!picked || picked.length === 0) return
    const room = max - files.length
    if (picked.length > room) message.warning(`At most ${max} files; ${room > 0 ? `only the first ${room} were added` : 'remove one to add another'}.`)
    const added = await Promise.all(Array.from(picked).slice(0, Math.max(room, 0)).map((f) => shrinkImage(f)))
    onChange([...files, ...added])
  }
  return (
    <div>
      <Flex gap={8} wrap>
        <Button size="large" icon={<CameraOutlined />} disabled={full} onClick={() => (canOpenCamera() ? setWebcam(true) : camera.current?.click())}>Take photo</Button>
        <Button size="large" icon={<UploadOutlined />} disabled={full} onClick={() => gallery.current?.click()}>Upload</Button>
        <Typography.Text type="secondary" style={{ alignSelf: 'center' }}>{files.length}/{max}</Typography.Text>
      </Flex>
      {/* "capture" opens the rear camera on a phone; the second input opens the photo library or files. */}
      <input ref={camera} type="file" accept="image/*" capture="environment" aria-label="Take a photo with the camera app" style={{ display: 'none' }} onChange={(e) => { void add(e.target.files); e.target.value = '' }} />
      <input ref={gallery} type="file" accept="image/jpeg,image/png,application/pdf" multiple aria-label="Upload photos or PDFs" style={{ display: 'none' }} onChange={(e) => { void add(e.target.files); e.target.value = '' }} />
      {webcam && !full && <WebcamModal onClose={() => setWebcam(false)} onPhoto={(f) => { void add([f]) }} onFallback={() => { setWebcam(false); camera.current?.click() }} />}
      {files.length > 0 && (
        <Flex gap={8} wrap style={{ marginTop: 12 }}>
          {files.map((f, i) => (
            <div key={`${i}-${f.name}-${f.size}`} style={{ position: 'relative', width: 84, height: 84, border: '1px solid rgba(128,128,128,0.35)', borderRadius: 8, overflow: 'hidden', display: 'grid', placeItems: 'center' }}>
              {previews[i] ? <img src={previews[i]} alt={f.name} style={{ width: '100%', height: '100%', objectFit: 'cover' }} /> : <Flex vertical align="center" style={{ padding: 4 }}><FilePdfOutlined style={{ fontSize: 24 }} /><Typography.Text ellipsis style={{ fontSize: 10, maxWidth: 76 }}>{f.name}</Typography.Text></Flex>}
              <Button size="small" shape="circle" aria-label={`Remove ${f.name}`} icon={<DeleteOutlined />} onClick={() => onChange(files.filter((_, j) => j !== i))} style={{ position: 'absolute', top: 2, right: 2 }} />
            </div>
          ))}
        </Flex>
      )}
    </div>
  )
}
