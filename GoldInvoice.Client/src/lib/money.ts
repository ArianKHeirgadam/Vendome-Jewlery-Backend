export const RIALS_PER_TOMAN = 10;

export function tomansToRials(value: number): number {
  if (!Number.isFinite(value)) throw new TypeError("A finite toman amount is required.");
  const rials = value * RIALS_PER_TOMAN;
  if (!Number.isSafeInteger(rials)) throw new RangeError("The amount is outside the supported range.");
  return rials;
}

export function rialsToTomans(value: number): number {
  return value / RIALS_PER_TOMAN;
}

export function activeNumberLocale(): "fa-IR" | "en-US" {
  return document.documentElement.lang === "en" ? "en-US" : "fa-IR";
}

export function formatTomansFromRials(value: number): string {
  const locale = activeNumberLocale();
  const amount = new Intl.NumberFormat(locale, { maximumFractionDigits: 1 }).format(rialsToTomans(value));
  return `${amount} ${locale === "fa-IR" ? "تومان" : "toman"}`;
}
