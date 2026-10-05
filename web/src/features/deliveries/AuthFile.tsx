import { useQuery } from '@tanstack/react-query'
import { Alert, Skeleton } from 'antd'
import { useEffect, useState } from 'react'
import { deliveriesApi } from '@/lib/api/endpoints'

/** Shows a stored file. Files are never public: the browser asks for them with the user's token and shows the bytes it is given. */
export function AuthFile({ url, name, contentType, height = 420 }: { url: string; name: string; contentType: string; height?: number }) {
  const file = useQuery({ queryKey: ['deliveries', 'file', url], queryFn: () => deliveriesApi.fetchFile(url), staleTime: 5 * 60_000 })
  const [objectUrl, setObjectUrl] = useState<string>()

  useEffect(() => {
    if (!file.data) return
    const created = URL.createObjectURL(file.data)
    setObjectUrl(created)
    return () => URL.revokeObjectURL(created)
  }, [file.data])

  if (file.isError) return <Alert type="warning" showIcon title="The file could not be loaded." />
  if (!objectUrl) return <Skeleton.Image active style={{ width: '100%', height: 160 }} />
  return contentType === 'application/pdf' ? (
    <iframe title={name} src={objectUrl} style={{ width: '100%', height, border: 0 }} />
  ) : (
    <img alt={name} src={objectUrl} style={{ maxWidth: '100%', maxHeight: height, objectFit: 'contain' }} />
  )
}
