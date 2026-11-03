import { ReactNode } from "react";
import styles from "./Button.module.css";

interface ButtonProps {
  children: ReactNode;
  variant?: "primary" | "secondary";
  onClick?: () => void;
  className?: string;
}

export default function Button({
  children,
  variant = "primary",
  onClick,
  className,
}: ButtonProps) {
  const buttonClass =
    variant === "primary" ? styles.btnPrimary : styles.btnSecondary;

  return (
    <button className={`${buttonClass} ${className || ""}`} onClick={onClick}>
      {children}
    </button>
  );
}
