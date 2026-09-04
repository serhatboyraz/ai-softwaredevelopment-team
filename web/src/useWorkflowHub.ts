import { useEffect, useState } from 'react'
import * as signalR from '@microsoft/signalr'

export type LiveEvent = {
  workflowId: string
  eventType: string
  message?: string | null
  occurredAt: string
}

export function useWorkflowHub(workflowId: string | undefined, onEvent: (e: LiveEvent) => void) {
  const [connected, setConnected] = useState(false)

  useEffect(() => {
    if (!workflowId) return

    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/workflowHub')
      .withAutomaticReconnect()
      .build()

    let cancelled = false

    connection.on('workflowEvent', (payload: LiveEvent) => {
      onEvent(payload)
    })

    ;(async () => {
      try {
        await connection.start()
        if (cancelled) return
        await connection.invoke('Subscribe', workflowId)
        setConnected(true)
      } catch {
        setConnected(false)
      }
    })()

    return () => {
      cancelled = true
      void connection.stop()
      setConnected(false)
    }
  }, [workflowId, onEvent])

  return connected
}
