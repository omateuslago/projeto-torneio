import { Campeonato } from "@/types";
import styles from "./Badge.module.css";

interface BadgeProps {
  status: Campeonato["status"];
  className?: string;
}

export default function Badge({ status, className = "" }: BadgeProps) {
  const statusStyles: Record<Campeonato["status"], string> = {
    "Aberto": styles.statusAberto,
    "Em andamento": styles.statusEmAndamento,
    "Encerrado": styles.statusEncerrado,
  };

  return (
    <span className={`${statusStyles[status]} ${className}`}>{status}</span>
  );
}
