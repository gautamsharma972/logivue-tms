/** The LogiVue TMS logo. `full` has the wordmark; `mark` is the truck and pin alone, for tight spaces. Replace the files in /public to change it everywhere. */
export function Logo({ variant = 'full', height = 64, style }: { variant?: 'full' | 'mark'; height?: number; style?: React.CSSProperties }) {
  return <img src={variant === 'full' ? '/logivue-logo.svg' : '/logivue-mark.svg'} alt="LogiVue TMS" height={height} style={{ height, width: 'auto', display: 'block', ...style }} />
}
