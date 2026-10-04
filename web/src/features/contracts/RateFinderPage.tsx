import { PageHeader } from '@/components/PageHeader'
import { QuotePanel } from './QuotePanel'

export function RateFinderPage() {
  return (
    <>
      <PageHeader
        title="Rate finder"
        description="Price a shipment against every contract in force on its date and compare transporters, cheapest first, with the maths shown."
      />
      <QuotePanel />
    </>
  )
}
